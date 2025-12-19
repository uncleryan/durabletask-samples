-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PostgreSQL Functions and Procedures for Durable Task Framework - Part 4
-- Activity task operations and query functions
-- Note: $(SchemaName) will be replaced at runtime with the actual schema name

-- ========================================
-- ACTIVITY TASK OPERATIONS
-- ========================================

-- Lock next activity task for processing
CREATE OR REPLACE FUNCTION $(SchemaName)._LockNextTask(
    p_locked_by VARCHAR(100),
    p_lock_expiration TIMESTAMP WITH TIME ZONE
)
RETURNS TABLE (
    SequenceNumber BIGINT,
    InstanceID VARCHAR(100),
    ExecutionID VARCHAR(50),
    Name VARCHAR(300),
    EventType VARCHAR(40),
    TaskID INT,
    VisibleTime TIMESTAMP WITH TIME ZONE,
    Timestamp TIMESTAMP WITH TIME ZONE,
    DequeueCount INT,
    Version VARCHAR(100),
    PayloadText TEXT,
    WaitTime INT,
    TraceContext VARCHAR(800)
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_now TIMESTAMP WITH TIME ZONE;
    v_sequence_number BIGINT;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    v_now := NOW() AT TIME ZONE 'UTC';

    -- Lock and update a single task
    UPDATE $(SchemaName).NewTasks NT
    SET
        LockedBy = p_locked_by,
        LockExpiration = p_lock_expiration,
        DequeueCount = DequeueCount + 1
    FROM (
        SELECT NT2.SequenceNumber
        FROM $(SchemaName).NewTasks NT2
        WHERE
            NT2.TaskHub = v_task_hub
            AND (NT2.LockExpiration IS NULL OR NT2.LockExpiration < v_now)
            AND (NT2.VisibleTime IS NULL OR NT2.VisibleTime < v_now)
        ORDER BY NT2.SequenceNumber
        LIMIT 1
        FOR UPDATE SKIP LOCKED
    ) AS selected
    WHERE NT.SequenceNumber = selected.SequenceNumber
    RETURNING NT.SequenceNumber INTO v_sequence_number;

    IF v_sequence_number IS NULL THEN
        RETURN;
    END IF;

    -- Return the locked task details
    RETURN QUERY
    SELECT
        N.SequenceNumber,
        N.InstanceID,
        N.ExecutionID,
        N.Name,
        'TaskScheduled'::VARCHAR(40) AS EventType,
        N.TaskID,
        N.VisibleTime,
        N.Timestamp,
        N.DequeueCount,
        N.Version,
        (SELECT P.Text FROM $(SchemaName).Payloads P 
         WHERE P.TaskHub = v_task_hub 
           AND P.InstanceID = N.InstanceID 
           AND P.PayloadID = N.PayloadID 
         LIMIT 1) AS PayloadText,
        EXTRACT(EPOCH FROM (v_now - N.Timestamp))::INT AS WaitTime,
        N.TraceContext
    FROM $(SchemaName).NewTasks N
    WHERE N.TaskHub = v_task_hub AND N.SequenceNumber = v_sequence_number;
END;
$$;

-- Renew orchestration locks
CREATE OR REPLACE FUNCTION $(SchemaName)._RenewOrchestrationLocks(
    p_instance_id VARCHAR(100),
    p_lock_expiration TIMESTAMP WITH TIME ZONE
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    UPDATE $(SchemaName).Instances
    SET LockExpiration = p_lock_expiration
    WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id;
END;
$$;

-- Renew task locks
CREATE OR REPLACE FUNCTION $(SchemaName)._RenewTaskLocks(
    p_renewing_tasks JSONB, -- Array of {SequenceNumber}
    p_lock_expiration TIMESTAMP WITH TIME ZONE
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    UPDATE $(SchemaName).NewTasks N
    SET LockExpiration = p_lock_expiration
    FROM JSONB_ARRAY_ELEMENTS(p_renewing_tasks) AS task
    WHERE N.TaskHub = v_task_hub 
      AND N.SequenceNumber = (task->>'SequenceNumber')::BIGINT;
END;
$$;

-- Complete activity tasks
CREATE OR REPLACE FUNCTION $(SchemaName)._CompleteTasks(
    p_completed_tasks JSONB, -- Array of {SequenceNumber}
    p_results JSONB -- Array of task result objects
)
RETURNS TABLE (
    DeletedSequenceNumber BIGINT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_existing_instance_id VARCHAR(100);
    v_deleted_count INT;
    v_expected_count INT;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    -- Ensure the instance exists and is running before attempting to handle task results
    SELECT R.InstanceID INTO v_existing_instance_id
    FROM $(SchemaName).Instances I
    INNER JOIN (
        SELECT DISTINCT (result->>'InstanceID')::VARCHAR(100) AS InstanceID,
                       (result->>'ExecutionID')::VARCHAR(50) AS ExecutionID
        FROM JSONB_ARRAY_ELEMENTS(p_results) AS result
    ) R ON 
        I.TaskHub = v_task_hub
        AND I.InstanceID = R.InstanceID
        AND I.ExecutionID = R.ExecutionID
        AND I.RuntimeStatus IN ('Running', 'Suspended')
    FOR UPDATE OF I
    LIMIT 1;

    -- If we find the instance, save the results to the NewEvents table
    IF v_existing_instance_id IS NOT NULL THEN
        -- Insert result payloads
        INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text, Reason)
        SELECT 
            v_task_hub,
            (result->>'InstanceID')::VARCHAR(100),
            (result->>'PayloadID')::UUID,
            result->>'PayloadText',
            result->>'Reason'
        FROM JSONB_ARRAY_ELEMENTS(p_results) AS result
        WHERE (result->>'PayloadID')::TEXT IS NOT NULL;

        -- Insert completion events
        INSERT INTO $(SchemaName).NewEvents (
            TaskHub,
            InstanceID,
            ExecutionID,
            Name,
            EventType,
            TaskID,
            VisibleTime,
            PayloadID
        )
        SELECT
            v_task_hub,
            (result->>'InstanceID')::VARCHAR(100),
            (result->>'ExecutionID')::VARCHAR(50),
            (result->>'Name')::VARCHAR(300),
            (result->>'EventType')::VARCHAR(40),
            (result->>'TaskID')::INT,
            (result->>'VisibleTime')::TIMESTAMP WITH TIME ZONE,
            (result->>'PayloadID')::UUID
        FROM JSONB_ARRAY_ELEMENTS(p_results) AS result;
    END IF;

    -- Delete completed tasks and return deleted sequence numbers
    SELECT JSONB_ARRAY_LENGTH(p_completed_tasks) INTO v_expected_count;

    RETURN QUERY
    DELETE FROM $(SchemaName).NewTasks N
    USING JSONB_ARRAY_ELEMENTS(p_completed_tasks) AS task
    WHERE N.TaskHub = v_task_hub
      AND N.SequenceNumber = (task->>'SequenceNumber')::BIGINT
    RETURNING N.SequenceNumber;

    GET DIAGNOSTICS v_deleted_count = ROW_COUNT;

    -- If we failed to delete all expected messages, abort
    IF v_deleted_count <> v_expected_count THEN
        RAISE EXCEPTION 'Failed to delete the completed task event(s). They may have been deleted by another worker, in which case the current execution is likely a duplicate. Expected: %, Deleted: %', v_expected_count, v_deleted_count;
    END IF;
END;
$$;

-- ========================================
-- QUERY OPERATIONS
-- ========================================

-- Query a single orchestration instance
CREATE OR REPLACE FUNCTION $(SchemaName).QuerySingleOrchestration(
    p_instance_id VARCHAR(100),
    p_execution_id VARCHAR(50) DEFAULT NULL,
    p_fetch_input BOOLEAN DEFAULT TRUE,
    p_fetch_output BOOLEAN DEFAULT TRUE
)
RETURNS TABLE (
    InstanceID VARCHAR(100),
    ExecutionID VARCHAR(50),
    Name VARCHAR(300),
    Version VARCHAR(100),
    CreatedTime TIMESTAMP WITH TIME ZONE,
    LastUpdatedTime TIMESTAMP WITH TIME ZONE,
    CompletedTime TIMESTAMP WITH TIME ZONE,
    RuntimeStatus VARCHAR(20),
    ParentInstanceID VARCHAR(100),
    CustomStatusText TEXT,
    InputText TEXT,
    OutputText TEXT,
    TraceContext VARCHAR(800)
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    RETURN QUERY
    SELECT
        I.InstanceID,
        I.ExecutionID,
        I.Name,
        I.Version,
        I.CreatedTime,
        I.LastUpdatedTime,
        I.CompletedTime,
        I.RuntimeStatus,
        I.ParentInstanceID,
        (SELECT P.Text FROM $(SchemaName).Payloads P 
         WHERE P.TaskHub = v_task_hub 
           AND P.InstanceID = I.InstanceID 
           AND P.PayloadID = I.CustomStatusPayloadID 
         LIMIT 1) AS CustomStatusText,
        CASE WHEN p_fetch_input THEN 
            (SELECT P.Text FROM $(SchemaName).Payloads P 
             WHERE P.TaskHub = v_task_hub 
               AND P.InstanceID = I.InstanceID 
               AND P.PayloadID = I.InputPayloadID 
             LIMIT 1)
        ELSE NULL END AS InputText,
        CASE WHEN p_fetch_output THEN 
            (SELECT P.Text FROM $(SchemaName).Payloads P 
             WHERE P.TaskHub = v_task_hub 
               AND P.InstanceID = I.InstanceID 
               AND P.PayloadID = I.OutputPayloadID 
             LIMIT 1)
        ELSE NULL END AS OutputText,
        I.TraceContext
    FROM $(SchemaName).Instances I
    WHERE
        I.TaskHub = v_task_hub
        AND I.InstanceID = p_instance_id
        AND (p_execution_id IS NULL OR p_execution_id = I.ExecutionID)
    LIMIT 1;
END;
$$;

-- Query multiple orchestration instances with filtering
CREATE OR REPLACE FUNCTION $(SchemaName)._QueryManyOrchestrations(
    p_page_size SMALLINT DEFAULT 100,
    p_page_number INT DEFAULT 0,
    p_fetch_input BOOLEAN DEFAULT TRUE,
    p_fetch_output BOOLEAN DEFAULT TRUE,
    p_created_time_from TIMESTAMP WITH TIME ZONE DEFAULT NULL,
    p_created_time_to TIMESTAMP WITH TIME ZONE DEFAULT NULL,
    p_runtime_status_filter TEXT DEFAULT NULL, -- Comma-separated list
    p_instance_id_prefix VARCHAR(100) DEFAULT NULL,
    p_exclude_sub_orchestrations BOOLEAN DEFAULT FALSE
)
RETURNS TABLE (
    InstanceID VARCHAR(100),
    ExecutionID VARCHAR(50),
    Name VARCHAR(300),
    Version VARCHAR(100),
    CreatedTime TIMESTAMP WITH TIME ZONE,
    LastUpdatedTime TIMESTAMP WITH TIME ZONE,
    CompletedTime TIMESTAMP WITH TIME ZONE,
    RuntimeStatus VARCHAR(20),
    ParentInstanceID VARCHAR(100),
    CustomStatusText TEXT,
    InputText TEXT,
    OutputText TEXT,
    TraceContext VARCHAR(800)
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_offset INT;
    v_status_array TEXT[];
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    v_offset := p_page_number * p_page_size;

    -- Convert comma-separated status filter to array
    IF p_runtime_status_filter IS NOT NULL THEN
        v_status_array := STRING_TO_ARRAY(p_runtime_status_filter, ',');
    END IF;

    RETURN QUERY
    SELECT
        I.InstanceID,
        I.ExecutionID,
        I.Name,
        I.Version,
        I.CreatedTime,
        I.LastUpdatedTime,
        I.CompletedTime,
        I.RuntimeStatus,
        I.ParentInstanceID,
        (SELECT P.Text FROM $(SchemaName).Payloads P 
         WHERE P.TaskHub = v_task_hub 
           AND P.InstanceID = I.InstanceID 
           AND P.PayloadID = I.CustomStatusPayloadID 
         LIMIT 1) AS CustomStatusText,
        CASE WHEN p_fetch_input THEN 
            (SELECT P.Text FROM $(SchemaName).Payloads P 
             WHERE P.TaskHub = v_task_hub 
               AND P.InstanceID = I.InstanceID 
               AND P.PayloadID = I.InputPayloadID 
             LIMIT 1)
        ELSE NULL END AS InputText,
        CASE WHEN p_fetch_output THEN 
            (SELECT P.Text FROM $(SchemaName).Payloads P 
             WHERE P.TaskHub = v_task_hub 
               AND P.InstanceID = I.InstanceID 
               AND P.PayloadID = I.OutputPayloadID 
             LIMIT 1)
        ELSE NULL END AS OutputText,
        I.TraceContext
    FROM $(SchemaName).Instances I
    WHERE
        I.TaskHub = v_task_hub
        AND (p_created_time_from IS NULL OR I.CreatedTime >= p_created_time_from)
        AND (p_created_time_to IS NULL OR I.CreatedTime <= p_created_time_to)
        AND (v_status_array IS NULL OR I.RuntimeStatus = ANY(v_status_array))
        AND (p_instance_id_prefix IS NULL OR I.InstanceID LIKE p_instance_id_prefix || '%')
        AND (NOT p_exclude_sub_orchestrations OR I.ParentInstanceID IS NULL)
    ORDER BY I.CreatedTime
    LIMIT p_page_size
    OFFSET v_offset;
END;
$$;

-- ========================================
-- DISCARD EVENTS AND UNLOCK
-- ========================================

-- Discard events and unlock instance (used when discarding duplicate work items)
CREATE OR REPLACE FUNCTION $(SchemaName)._DiscardEventsAndUnlockInstance(
    p_instance_id VARCHAR(100),
    p_deleted_events JSONB -- Array of {InstanceID, SequenceNumber}
)
RETURNS TABLE (
    DeletedInstanceID VARCHAR(100),
    DeletedSequenceNumber BIGINT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_now TIMESTAMP WITH TIME ZONE;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    v_now := NOW() AT TIME ZONE 'UTC';

    -- Delete events and return what was deleted
    RETURN QUERY
    DELETE FROM $(SchemaName).NewEvents E
    USING JSONB_ARRAY_ELEMENTS(p_deleted_events) AS deleted
    WHERE E.TaskHub = v_task_hub
      AND E.InstanceID = (deleted->>'InstanceID')::VARCHAR(100)
      AND E.SequenceNumber = (deleted->>'SequenceNumber')::BIGINT
    RETURNING E.InstanceID, E.SequenceNumber;

    -- Release the lock on this instance
    UPDATE $(SchemaName).Instances
    SET LastUpdatedTime = v_now, LockExpiration = NULL
    WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id;
END;
$$;

-- ========================================
-- ADD ORCHESTRATION EVENTS
-- ========================================

-- Add orchestration events (for sending messages/events to orchestrations)
CREATE OR REPLACE FUNCTION $(SchemaName)._AddOrchestrationEvents(
    p_new_orchestration_events JSONB -- Array of orchestration event objects
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_event JSONB;
    v_auto_start_name VARCHAR(300);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    -- Create auto-start instances for external events
    FOR v_event IN SELECT * FROM JSONB_ARRAY_ELEMENTS(p_new_orchestration_events)
    LOOP
        IF LEFT((v_event->>'InstanceID')::TEXT, 1) = '@' 
           AND POSITION('@' IN SUBSTRING((v_event->>'InstanceID')::TEXT FROM 2)) > 0 THEN
            
            v_auto_start_name := SUBSTRING(
                (v_event->>'InstanceID')::TEXT,
                2,
                POSITION('@' IN SUBSTRING((v_event->>'InstanceID')::TEXT FROM 2))
            );

            INSERT INTO $(SchemaName).Instances (
                TaskHub,
                InstanceID,
                ExecutionID,
                Name,
                Version,
                RuntimeStatus,
                TraceContext
            ) VALUES (
                v_task_hub,
                (v_event->>'InstanceID')::VARCHAR(100),
                gen_random_uuid()::TEXT,
                v_auto_start_name,
                '',
                'Pending',
                (v_event->>'TraceContext')::VARCHAR(800)
            )
            ON CONFLICT (TaskHub, InstanceID) DO NOTHING;
        END IF;
    END LOOP;

    -- Insert event payloads
    INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text, Reason)
    SELECT 
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'PayloadID')::UUID,
        event->>'PayloadText',
        event->>'Reason'
    FROM JSONB_ARRAY_ELEMENTS(p_new_orchestration_events) AS event
    WHERE (event->>'PayloadID')::TEXT IS NOT NULL;

    -- Insert the events
    INSERT INTO $(SchemaName).NewEvents (
        TaskHub,
        InstanceID,
        ExecutionID,
        EventType,
        Name,
        RuntimeStatus,
        VisibleTime,
        TaskID,
        TraceContext,
        PayloadID
    )
    SELECT 
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'ExecutionID')::VARCHAR(50),
        (event->>'EventType')::VARCHAR(40),
        (event->>'Name')::VARCHAR(300),
        (event->>'RuntimeStatus')::VARCHAR(30),
        (event->>'VisibleTime')::TIMESTAMP WITH TIME ZONE,
        (event->>'TaskID')::INT,
        (event->>'TraceContext')::VARCHAR(800),
        (event->>'PayloadID')::UUID
    FROM JSONB_ARRAY_ELEMENTS(p_new_orchestration_events) AS event;
END;
$$;

