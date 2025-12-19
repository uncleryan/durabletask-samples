-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PostgreSQL Functions and Procedures for Durable Task Framework - Part 3
-- Orchestration work item locking and checkpoint operations
-- Note: $(SchemaName) will be replaced at runtime with the actual schema name

-- ========================================
-- WORK ITEM LOCKING
-- ========================================

-- Lock next orchestration for processing
-- This is one of the most critical functions for the orchestration runtime
CREATE OR REPLACE FUNCTION $(SchemaName)._LockNextOrchestration(
    p_batch_size INT,
    p_locked_by VARCHAR(100),
    p_lock_expiration TIMESTAMP WITH TIME ZONE
)
RETURNS TABLE (
    ResultSet INT,
    SequenceNumber BIGINT,
    Timestamp TIMESTAMP WITH TIME ZONE,
    VisibleTime TIMESTAMP WITH TIME ZONE,
    DequeueCount INT,
    InstanceID VARCHAR(100),
    ExecutionID VARCHAR(50),
    EventType VARCHAR(40),
    Name VARCHAR(300),
    RuntimeStatus VARCHAR(30),
    TaskID INT,
    Reason TEXT,
    PayloadText TEXT,
    PayloadID UUID,
    WaitTime INT,
    ParentInstanceID VARCHAR(100),
    Version VARCHAR(100),
    TraceContext VARCHAR(800)
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_now TIMESTAMP WITH TIME ZONE;
    v_instance_id VARCHAR(100);
    v_parent_instance_id VARCHAR(100);
    v_version VARCHAR(100);
    v_runtime_status VARCHAR(30);
    v_event_count INT;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    v_now := NOW() AT TIME ZONE 'UTC';

    -- Lock the first active instance that has pending messages
    -- Skip locked rows to avoid blocking
    WITH locked_instance AS (
        SELECT I.InstanceID, I.ParentInstanceID, I.RuntimeStatus, I.Version
        FROM $(SchemaName).Instances I
        INNER JOIN $(SchemaName).NewEvents E ON
            E.TaskHub = v_task_hub AND
            E.InstanceID = I.InstanceID
        WHERE
            I.TaskHub = v_task_hub
            AND (I.LockExpiration IS NULL OR I.LockExpiration < v_now)
            AND (E.VisibleTime IS NULL OR E.VisibleTime < v_now)
        ORDER BY E.SequenceNumber
        LIMIT 1
        FOR UPDATE OF I SKIP LOCKED
    )
    UPDATE $(SchemaName).Instances I
    SET
        LockedBy = p_locked_by,
        LockExpiration = p_lock_expiration
    FROM locked_instance li
    WHERE I.TaskHub = v_task_hub AND I.InstanceID = li.InstanceID
    RETURNING I.InstanceID, I.ParentInstanceID, I.RuntimeStatus, I.Version
    INTO v_instance_id, v_parent_instance_id, v_runtime_status, v_version;

    -- If no instance was locked, return empty result
    IF v_instance_id IS NULL THEN
        RETURN;
    END IF;

    -- Result Set #1: The list of new events to fetch (limited by batch size)
    RETURN QUERY
    SELECT
        1 AS ResultSet,
        N.SequenceNumber,
        N.Timestamp,
        N.VisibleTime,
        N.DequeueCount,
        N.InstanceID,
        N.ExecutionID,
        N.EventType,
        N.Name,
        N.RuntimeStatus,
        N.TaskID,
        P.Reason,
        P.Text AS PayloadText,
        P.PayloadID,
        EXTRACT(EPOCH FROM (v_now - N.Timestamp))::INT AS WaitTime,
        v_parent_instance_id AS ParentInstanceID,
        v_version AS Version,
        N.TraceContext
    FROM $(SchemaName).NewEvents N
        LEFT OUTER JOIN $(SchemaName).Payloads P ON 
            P.TaskHub = v_task_hub AND
            P.InstanceID = N.InstanceID AND
            P.PayloadID = N.PayloadID
    WHERE
        N.TaskHub = v_task_hub
        AND N.InstanceID = v_instance_id
        AND (N.VisibleTime IS NULL OR N.VisibleTime < v_now)
    ORDER BY N.SequenceNumber
    LIMIT p_batch_size;

    GET DIAGNOSTICS v_event_count = ROW_COUNT;

    -- If no events were returned, bail out
    IF v_event_count = 0 THEN
        RETURN;
    END IF;

    -- Result Set #2: Basic information about this instance
    RETURN QUERY
    SELECT
        2 AS ResultSet,
        NULL::BIGINT,
        NULL::TIMESTAMP WITH TIME ZONE,
        NULL::TIMESTAMP WITH TIME ZONE,
        NULL::INT,
        v_instance_id AS InstanceID,
        NULL::VARCHAR(50),
        NULL::VARCHAR(40),
        NULL::VARCHAR(300),
        v_runtime_status AS RuntimeStatus,
        NULL::INT,
        NULL::TEXT,
        NULL::TEXT,
        NULL::UUID,
        NULL::INT,
        NULL::VARCHAR(100),
        NULL::VARCHAR(100),
        NULL::VARCHAR(800);

    -- Result Set #3: The full event history for the locked instance
    RETURN QUERY
    SELECT
        3 AS ResultSet,
        NULL::BIGINT,
        H.Timestamp,
        H.VisibleTime,
        NULL::INT,
        H.InstanceID,
        H.ExecutionID,
        H.EventType,
        H.Name,
        H.RuntimeStatus,
        H.TaskID,
        P.Reason,
        -- Optimization: Do not load data payloads for TaskScheduled/SubOrchestrationInstanceCreated
        CASE WHEN H.EventType IN ('TaskScheduled', 'SubOrchestrationInstanceCreated') 
             THEN NULL ELSE P.Text END AS PayloadText,
        H.DataPayloadID AS PayloadID,
        NULL::INT,
        v_parent_instance_id AS ParentInstanceID,
        v_version AS Version,
        H.TraceContext
    FROM $(SchemaName).History H
        LEFT OUTER JOIN $(SchemaName).Payloads P ON
            P.TaskHub = v_task_hub AND
            P.InstanceID = H.InstanceID AND
            P.PayloadID = H.DataPayloadID
    WHERE 
        H.TaskHub = v_task_hub 
        AND H.InstanceID = v_instance_id
    ORDER BY H.SequenceNumber ASC;
END;
$$;

-- ========================================
-- CHECKPOINT ORCHESTRATION
-- ========================================

-- Checkpoint orchestration state (save history, send new events/tasks)
-- This function handles the complex logic of saving orchestration state
CREATE OR REPLACE FUNCTION $(SchemaName)._CheckpointOrchestration(
    p_instance_id VARCHAR(100),
    p_execution_id VARCHAR(50),
    p_runtime_status VARCHAR(30),
    p_custom_status_payload TEXT,
    p_deleted_events JSONB, -- Array of {InstanceID, SequenceNumber}
    p_new_history_events JSONB, -- Array of history event objects
    p_new_orchestration_events JSONB, -- Array of orchestration event objects
    p_new_task_events JSONB -- Array of task event objects
)
RETURNS TABLE (
    DeletedInstanceID VARCHAR(100),
    DeletedSequenceNumber BIGINT,
    NewTaskSequenceNumber BIGINT,
    NewTaskTaskID INT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_input_payload_id UUID;
    v_custom_status_payload_id UUID;
    v_existing_output_payload_id UUID;
    v_existing_custom_status_payload TEXT;
    v_existing_execution_id VARCHAR(50);
    v_is_continue_as_new BOOLEAN := FALSE;
    v_is_completed BOOLEAN;
    v_output_payload_id UUID;
    v_now TIMESTAMP WITH TIME ZONE;
    v_event JSONB;
    v_auto_start_name VARCHAR(300);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    v_now := NOW() AT TIME ZONE 'UTC';

    -- Get existing instance information
    SELECT 
        I.InputPayloadID,
        I.CustomStatusPayloadID,
        I.OutputPayloadID,
        P.Text,
        I.ExecutionID
    INTO 
        v_input_payload_id,
        v_custom_status_payload_id,
        v_existing_output_payload_id,
        v_existing_custom_status_payload,
        v_existing_execution_id
    FROM $(SchemaName).Instances I
        LEFT OUTER JOIN $(SchemaName).Payloads P ON
            P.TaskHub = v_task_hub AND
            P.InstanceID = I.InstanceID AND
            P.PayloadID = I.CustomStatusPayloadID
    WHERE I.TaskHub = v_task_hub AND I.InstanceID = p_instance_id
    FOR UPDATE OF I;

    -- Check for ContinueAsNew
    IF v_existing_execution_id IS NOT NULL AND v_existing_execution_id <> p_execution_id THEN
        v_is_continue_as_new := TRUE;

        -- Delete old history and associated payloads
        DELETE FROM $(SchemaName).History
        WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id;

        -- Delete old payloads
        DELETE FROM $(SchemaName).Payloads
        WHERE TaskHub = v_task_hub 
          AND InstanceID = p_instance_id 
          AND PayloadID IN (v_input_payload_id, v_custom_status_payload_id, v_existing_output_payload_id);

        v_existing_custom_status_payload := NULL;
    END IF;

    -- Handle custom status payload
    IF v_existing_custom_status_payload IS NULL AND p_custom_status_payload IS NOT NULL THEN
        v_custom_status_payload_id := gen_random_uuid();
        INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text)
        VALUES (v_task_hub, p_instance_id, v_custom_status_payload_id, p_custom_status_payload);
    ELSIF v_existing_custom_status_payload IS NOT NULL AND v_existing_custom_status_payload <> p_custom_status_payload THEN
        UPDATE $(SchemaName).Payloads 
        SET Text = p_custom_status_payload
        WHERE TaskHub = v_task_hub 
          AND InstanceID = p_instance_id 
          AND PayloadID = v_custom_status_payload_id;
    END IF;

    -- Update input payload ID for ContinueAsNew
    IF v_is_continue_as_new THEN
        SELECT (event->>'PayloadID')::UUID INTO v_input_payload_id
        FROM JSONB_ARRAY_ELEMENTS(p_new_history_events) AS event
        WHERE event->>'EventType' = 'ExecutionStarted'
        ORDER BY (event->>'SequenceNumber')::BIGINT DESC
        LIMIT 1;
    END IF;

    -- Check if orchestration is completed
    v_is_completed := (p_runtime_status IN ('Completed', 'Failed', 'Terminated'));

    -- Get output payload ID if completed
    IF v_is_completed THEN
        SELECT (event->>'PayloadID')::UUID INTO v_output_payload_id
        FROM JSONB_ARRAY_ELEMENTS(p_new_history_events) AS event
        WHERE event->>'EventType' IN ('ExecutionCompleted', 'ExecutionTerminated')
        ORDER BY (event->>'SequenceNumber')::BIGINT DESC
        LIMIT 1;
    END IF;

    -- Insert history event payloads
    INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text, Reason)
    SELECT 
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'PayloadID')::UUID,
        event->>'PayloadText',
        event->>'Reason'
    FROM JSONB_ARRAY_ELEMENTS(p_new_history_events) AS event
    WHERE event->>'PayloadText' IS NOT NULL OR event->>'Reason' IS NOT NULL;

    -- Update instance record
    UPDATE $(SchemaName).Instances
    SET
        ExecutionID = p_execution_id,
        RuntimeStatus = p_runtime_status,
        LastUpdatedTime = v_now,
        CompletedTime = CASE WHEN v_is_completed THEN v_now ELSE NULL END,
        LockExpiration = NULL, -- release the lock
        CustomStatusPayloadID = v_custom_status_payload_id,
        InputPayloadID = v_input_payload_id,
        OutputPayloadID = v_output_payload_id
    WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'The instance does not exist.';
    END IF;

    -- Create new instances for auto-start external events
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

    -- Create sub-orchestration instances
    INSERT INTO $(SchemaName).Instances (
        TaskHub,
        InstanceID,
        ExecutionID,
        Name,
        Version,
        ParentInstanceID,
        RuntimeStatus,
        TraceContext
    )
    SELECT DISTINCT
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'ExecutionID')::VARCHAR(50),
        (event->>'Name')::VARCHAR(300),
        (event->>'Version')::VARCHAR(100),
        (event->>'ParentInstanceID')::VARCHAR(100),
        'Pending',
        (event->>'TraceContext')::VARCHAR(800)
    FROM JSONB_ARRAY_ELEMENTS(p_new_orchestration_events) AS event
    WHERE (event->>'EventType')::TEXT = 'ExecutionStarted'
    ON CONFLICT (TaskHub, InstanceID) DO NOTHING;

    -- Insert orchestration event payloads
    INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text, Reason)
    SELECT 
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'PayloadID')::UUID,
        event->>'PayloadText',
        event->>'Reason'
    FROM JSONB_ARRAY_ELEMENTS(p_new_orchestration_events) AS event
    WHERE (event->>'PayloadID')::TEXT IS NOT NULL;

    -- Insert task event payloads
    INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text)
    SELECT 
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'PayloadID')::UUID,
        event->>'PayloadText'
    FROM JSONB_ARRAY_ELEMENTS(p_new_task_events) AS event
    WHERE (event->>'PayloadID')::TEXT IS NOT NULL;

    -- Insert new orchestration events
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

    -- Delete processed events and return them
    RETURN QUERY
    DELETE FROM $(SchemaName).NewEvents E
    USING JSONB_ARRAY_ELEMENTS(p_deleted_events) AS deleted
    WHERE E.TaskHub = v_task_hub
      AND E.InstanceID = (deleted->>'InstanceID')::VARCHAR(100)
      AND E.SequenceNumber = (deleted->>'SequenceNumber')::BIGINT
    RETURNING E.InstanceID, E.SequenceNumber, NULL::BIGINT, NULL::INT;

    -- Insert history events (this can fail with PK violation in split-brain scenarios)
    INSERT INTO $(SchemaName).History (
        TaskHub,
        InstanceID,
        ExecutionID,
        SequenceNumber,
        EventType,
        TaskID,
        Timestamp,
        IsPlayed,
        Name,
        RuntimeStatus,
        VisibleTime,
        TraceContext,
        DataPayloadID
    )
    SELECT
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'ExecutionID')::VARCHAR(50),
        (event->>'SequenceNumber')::BIGINT,
        (event->>'EventType')::VARCHAR(40),
        (event->>'TaskID')::INT,
        (event->>'Timestamp')::TIMESTAMP WITH TIME ZONE,
        (event->>'IsPlayed')::BOOLEAN,
        (event->>'Name')::VARCHAR(300),
        (event->>'RuntimeStatus')::VARCHAR(20),
        (event->>'VisibleTime')::TIMESTAMP WITH TIME ZONE,
        (event->>'TraceContext')::VARCHAR(800),
        (event->>'PayloadID')::UUID
    FROM JSONB_ARRAY_ELEMENTS(p_new_history_events) AS event;

    -- Insert new tasks and return their sequence numbers
    RETURN QUERY
    INSERT INTO $(SchemaName).NewTasks (
        TaskHub,
        InstanceID,
        ExecutionID,
        Name,
        TaskID,
        VisibleTime,
        LockedBy,
        LockExpiration,
        PayloadID,
        Version,
        TraceContext
    )
    SELECT 
        v_task_hub,
        (event->>'InstanceID')::VARCHAR(100),
        (event->>'ExecutionID')::VARCHAR(50),
        (event->>'Name')::VARCHAR(300),
        (event->>'TaskID')::INT,
        (event->>'VisibleTime')::TIMESTAMP WITH TIME ZONE,
        (event->>'LockedBy')::VARCHAR(100),
        (event->>'LockExpiration')::TIMESTAMP WITH TIME ZONE,
        (event->>'PayloadID')::UUID,
        (event->>'Version')::VARCHAR(100),
        (event->>'TraceContext')::VARCHAR(800)
    FROM JSONB_ARRAY_ELEMENTS(p_new_task_events) AS event
    RETURNING NULL::VARCHAR(100), NULL::BIGINT, SequenceNumber, TaskID;
END;
$$;

