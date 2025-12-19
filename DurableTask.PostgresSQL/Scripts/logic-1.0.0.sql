-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PostgreSQL Functions and Procedures for Durable Task Framework
-- Note: $(SchemaName) will be replaced at runtime with the actual schema name

-- ========================================
-- HELPER FUNCTIONS
-- ========================================

-- Get the current task hub name based on configuration
CREATE OR REPLACE FUNCTION $(SchemaName).CurrentTaskHub()
RETURNS VARCHAR(50)
LANGUAGE plpgsql
AS $$
DECLARE
    task_hub_mode TEXT;
    task_hub VARCHAR(150);
BEGIN
    -- Task Hub modes:
    -- '0': Task hub names are set by the app (from application_name)
    -- '1': Task hub names are inferred from the user credential
    SELECT Value INTO task_hub_mode
    FROM $(SchemaName).GlobalSettings
    WHERE Name = 'TaskHubMode'
    LIMIT 1;

    IF task_hub_mode = '0' THEN
        task_hub := current_setting('application.name', true);
    ELSIF task_hub_mode = '1' THEN
        task_hub := current_user;
    END IF;

    IF task_hub IS NULL THEN
        task_hub := 'default';
    END IF;

    -- If the name is too long, keep the first 16 characters and hash the rest
    IF LENGTH(task_hub) > 50 THEN
        task_hub := SUBSTRING(task_hub, 1, 16) || '__' || MD5(task_hub);
    END IF;

    RETURN task_hub;
END;
$$;

-- Get scale metric (count of live instances + tasks)
CREATE OR REPLACE FUNCTION $(SchemaName).GetScaleMetric()
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    task_hub VARCHAR(50);
    now_utc TIMESTAMP WITH TIME ZONE;
    live_instances BIGINT := 0;
    live_tasks BIGINT := 0;
BEGIN
    task_hub := $(SchemaName).CurrentTaskHub();
    now_utc := NOW() AT TIME ZONE 'UTC';

    SELECT
        COUNT(DISTINCT E.InstanceID),
        COUNT(T.InstanceID)
    INTO live_instances, live_tasks
    FROM $(SchemaName).Instances I
        LEFT OUTER JOIN $(SchemaName).NewEvents E 
            ON E.TaskHub = task_hub AND E.InstanceID = I.InstanceID
        LEFT OUTER JOIN $(SchemaName).NewTasks T 
            ON T.TaskHub = task_hub AND T.InstanceID = I.InstanceID
    WHERE
        I.TaskHub = task_hub
        AND I.RuntimeStatus IN ('Pending', 'Running')
        AND (E.VisibleTime IS NULL OR now_utc > E.VisibleTime);

    RETURN COALESCE(live_instances, 0) + COALESCE(live_tasks, 0);
END;
$$;

-- Get scale recommendation based on workload
CREATE OR REPLACE FUNCTION $(SchemaName).GetScaleRecommendation(
    max_orchestrations_per_worker REAL,
    max_activities_per_worker REAL
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    task_hub VARCHAR(50);
    now_utc TIMESTAMP WITH TIME ZONE;
    live_instances BIGINT := 0;
    live_tasks BIGINT := 0;
    recommended_workers_for_orchestrations INT;
    recommended_workers_for_activities INT;
BEGIN
    task_hub := $(SchemaName).CurrentTaskHub();
    now_utc := NOW() AT TIME ZONE 'UTC';

    SELECT
        COUNT(DISTINCT E.InstanceID),
        COUNT(T.InstanceID)
    INTO live_instances, live_tasks
    FROM $(SchemaName).Instances I
        LEFT OUTER JOIN $(SchemaName).NewEvents E 
            ON E.TaskHub = task_hub AND E.InstanceID = I.InstanceID
        LEFT OUTER JOIN $(SchemaName).NewTasks T 
            ON T.TaskHub = task_hub AND T.InstanceID = I.InstanceID
    WHERE
        I.TaskHub = task_hub
        AND I.RuntimeStatus IN ('Pending', 'Running')
        AND (E.VisibleTime IS NULL OR E.VisibleTime < now_utc);

    IF max_orchestrations_per_worker < 1 THEN max_orchestrations_per_worker := 1; END IF;
    IF max_activities_per_worker < 1 THEN max_activities_per_worker := 1; END IF;

    recommended_workers_for_orchestrations := CEIL(COALESCE(live_instances, 0) / max_orchestrations_per_worker);
    recommended_workers_for_activities := CEIL(COALESCE(live_tasks, 0) / max_activities_per_worker);

    RETURN recommended_workers_for_orchestrations + recommended_workers_for_activities;
END;
$$;

-- ========================================
-- VIEWS
-- ========================================

-- View for querying instances with their payload text
CREATE OR REPLACE VIEW $(SchemaName).vInstances AS
SELECT
    I.TaskHub,
    I.InstanceID,
    I.ExecutionID,
    I.Name,
    I.Version,
    I.CreatedTime,
    I.LastUpdatedTime,
    I.CompletedTime,
    I.RuntimeStatus,
    I.TraceContext,
    (SELECT P.Text FROM $(SchemaName).Payloads P 
     WHERE P.TaskHub = $(SchemaName).CurrentTaskHub() 
       AND P.InstanceID = I.InstanceID 
       AND P.PayloadID = I.CustomStatusPayloadID 
     LIMIT 1) AS CustomStatusText,
    (SELECT P.Text FROM $(SchemaName).Payloads P 
     WHERE P.TaskHub = $(SchemaName).CurrentTaskHub() 
       AND P.InstanceID = I.InstanceID 
       AND P.PayloadID = I.InputPayloadID 
     LIMIT 1) AS InputText,
    (SELECT P.Text FROM $(SchemaName).Payloads P 
     WHERE P.TaskHub = $(SchemaName).CurrentTaskHub() 
       AND P.InstanceID = I.InstanceID 
       AND P.PayloadID = I.OutputPayloadID 
     LIMIT 1) AS OutputText,
    I.ParentInstanceID
FROM $(SchemaName).Instances I
WHERE I.TaskHub = $(SchemaName).CurrentTaskHub();

-- View for querying history with payload text
CREATE OR REPLACE VIEW $(SchemaName).vHistory AS
SELECT
    H.TaskHub,
    H.InstanceID,
    H.ExecutionID,
    H.SequenceNumber,
    H.EventType,
    H.TaskID,
    H.Timestamp,
    H.IsPlayed,
    H.Name,
    H.RuntimeStatus,
    H.VisibleTime,
    H.TraceContext,
    (SELECT P.Text FROM $(SchemaName).Payloads P 
     WHERE P.TaskHub = $(SchemaName).CurrentTaskHub() 
       AND P.InstanceID = H.InstanceID 
       AND P.PayloadID = H.DataPayloadID 
     LIMIT 1) AS Payload
FROM $(SchemaName).History H
WHERE H.TaskHub = $(SchemaName).CurrentTaskHub();

-- ========================================
-- VERSION TRACKING
-- ========================================

-- Get schema versions
CREATE OR REPLACE FUNCTION $(SchemaName)._GetVersions()
RETURNS TABLE (
    Major INT,
    Minor INT,
    Patch INT,
    Prerelease VARCHAR(100)
)
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN QUERY
    SELECT
        SPLIT_PART(SemanticVersion, '.', 1)::INT AS Major,
        SPLIT_PART(SemanticVersion, '.', 2)::INT AS Minor,
        SPLIT_PART(SPLIT_PART(SemanticVersion, '-', 1), '.', 3)::INT AS Patch,
        CASE 
            WHEN POSITION('-' IN SemanticVersion) > 0 
            THEN SUBSTRING(SemanticVersion FROM POSITION('-' IN SemanticVersion) + 1)
            ELSE NULL 
        END AS Prerelease
    FROM $(SchemaName).Versions
    ORDER BY UpgradeTime DESC;
END;
$$;

-- ========================================
-- INSTANCE MANAGEMENT
-- ========================================

-- Create a new orchestration instance
CREATE OR REPLACE FUNCTION $(SchemaName).CreateInstance(
    p_name VARCHAR(300),
    p_version VARCHAR(100) DEFAULT NULL,
    p_instance_id VARCHAR(100) DEFAULT NULL,
    p_execution_id VARCHAR(50) DEFAULT NULL,
    p_input_text TEXT DEFAULT NULL,
    p_start_time TIMESTAMP WITH TIME ZONE DEFAULT NULL,
    p_dedupe_statuses TEXT DEFAULT 'Pending,Running',
    p_trace_context VARCHAR(800) DEFAULT NULL
)
RETURNS TABLE (
    InstanceID VARCHAR(100),
    ExecutionID VARCHAR(50)
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_event_type VARCHAR(30) := 'ExecutionStarted';
    v_runtime_status VARCHAR(30) := 'Pending';
    v_input_payload_id UUID;
    v_existing_status VARCHAR(30);
    v_final_instance_id VARCHAR(100);
    v_final_execution_id VARCHAR(50);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    -- Check for instance ID collisions
    IF p_instance_id IS NULL THEN
        v_final_instance_id := gen_random_uuid()::TEXT;
    ELSE
        v_final_instance_id := p_instance_id;
        
        -- Check if instance already exists
        SELECT I.RuntimeStatus INTO v_existing_status
        FROM $(SchemaName).Instances I
        WHERE I.TaskHub = v_task_hub AND I.InstanceID = v_final_instance_id
        FOR UPDATE;

        -- Check if the existing instance is in a dedup status
        IF v_existing_status = ANY(STRING_TO_ARRAY(p_dedupe_statuses, ',')) THEN
            RAISE EXCEPTION 'Cannot create instance with ID ''%'' because a pending or running instance already exists.', v_final_instance_id;
        END IF;

        -- If instance exists in a terminal state, purge it
        IF v_existing_status IS NOT NULL THEN
            PERFORM $(SchemaName).PurgeInstanceStateByID(ARRAY[v_final_instance_id]);
        END IF;
    END IF;

    IF p_execution_id IS NULL THEN
        v_final_execution_id := gen_random_uuid()::TEXT;
    ELSE
        v_final_execution_id := p_execution_id;
    END IF;

    -- Insert payload if provided
    IF p_input_text IS NOT NULL THEN
        v_input_payload_id := gen_random_uuid();
        INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text)
        VALUES (v_task_hub, v_final_instance_id, v_input_payload_id, p_input_text);
    END IF;

    -- Insert instance record
    INSERT INTO $(SchemaName).Instances (
        Name,
        Version,
        TaskHub,
        InstanceID,
        ExecutionID,
        RuntimeStatus,
        InputPayloadID,
        TraceContext
    ) VALUES (
        p_name,
        p_version,
        v_task_hub,
        v_final_instance_id,
        v_final_execution_id,
        v_runtime_status,
        v_input_payload_id,
        p_trace_context
    );

    -- Insert execution started event
    INSERT INTO $(SchemaName).NewEvents (
        Name,
        TaskHub,
        InstanceID,
        ExecutionID,
        RuntimeStatus,
        VisibleTime,
        EventType,
        TraceContext,
        PayloadID
    ) VALUES (
        p_name,
        v_task_hub,
        v_final_instance_id,
        v_final_execution_id,
        v_runtime_status,
        p_start_time,
        v_event_type,
        p_trace_context,
        v_input_payload_id
    );

    RETURN QUERY SELECT v_final_instance_id, v_final_execution_id;
END;
$$;

