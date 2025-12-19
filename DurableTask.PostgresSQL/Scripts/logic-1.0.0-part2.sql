-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PostgreSQL Functions and Procedures for Durable Task Framework - Part 2
-- Additional instance management and orchestration functions
-- Note: $(SchemaName) will be replaced at runtime with the actual schema name

-- ========================================
-- INSTANCE OPERATIONS
-- ========================================

-- Get instance history
CREATE OR REPLACE FUNCTION $(SchemaName).GetInstanceHistory(
    p_instance_id VARCHAR(100),
    p_get_inputs_and_outputs BOOLEAN DEFAULT FALSE
)
RETURNS TABLE (
    InstanceID VARCHAR(100),
    ExecutionID VARCHAR(50),
    SequenceNumber BIGINT,
    EventType VARCHAR(40),
    Name VARCHAR(300),
    RuntimeStatus VARCHAR(20),
    TaskID INT,
    Timestamp TIMESTAMP WITH TIME ZONE,
    IsPlayed BOOLEAN,
    VisibleTime TIMESTAMP WITH TIME ZONE,
    Reason TEXT,
    PayloadText TEXT,
    PayloadID UUID,
    ParentInstanceID VARCHAR(100),
    Version VARCHAR(100),
    TraceContext VARCHAR(800)
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_parent_instance_id VARCHAR(100);
    v_version VARCHAR(100);
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    
    SELECT I.ParentInstanceID, I.Version
    INTO v_parent_instance_id, v_version
    FROM $(SchemaName).Instances I
    WHERE I.InstanceID = p_instance_id;

    RETURN QUERY
    SELECT
        H.InstanceID,
        H.ExecutionID,
        H.SequenceNumber,
        H.EventType,
        H.Name,
        H.RuntimeStatus,
        H.TaskID,
        H.Timestamp,
        H.IsPlayed,
        H.VisibleTime,
        P.Reason,
        CASE WHEN p_get_inputs_and_outputs THEN P.Text ELSE NULL END AS PayloadText,
        H.DataPayloadID AS PayloadID,
        v_parent_instance_id AS ParentInstanceID,
        v_version AS Version,
        H.TraceContext
    FROM $(SchemaName).History H
        LEFT OUTER JOIN $(SchemaName).Payloads P ON
            P.TaskHub = v_task_hub AND
            P.InstanceID = H.InstanceID AND
            P.PayloadID = H.DataPayloadID
    WHERE
        H.TaskHub = v_task_hub AND
        H.InstanceID = p_instance_id
    ORDER BY H.SequenceNumber ASC;
END;
$$;

-- Raise an external event
CREATE OR REPLACE FUNCTION $(SchemaName).RaiseEvent(
    p_name VARCHAR(300),
    p_instance_id VARCHAR(100),
    p_payload_text TEXT DEFAULT NULL,
    p_delivery_time TIMESTAMP WITH TIME ZONE DEFAULT NULL
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_payload_id UUID;
    v_auto_start_orchestration_name VARCHAR(300);
    v_instance_exists BOOLEAN;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    -- Check if instance exists
    SELECT EXISTS(
        SELECT 1
        FROM $(SchemaName).Instances I
        WHERE I.TaskHub = v_task_hub AND I.InstanceID = p_instance_id
    ) INTO v_instance_exists;

    -- If instance doesn't exist, check for auto-start format: @orchestrationname@identifier
    IF NOT v_instance_exists THEN
        IF LEFT(p_instance_id, 1) = '@' AND POSITION('@' IN SUBSTRING(p_instance_id FROM 2)) > 0 THEN
            v_auto_start_orchestration_name := SUBSTRING(
                p_instance_id,
                2,
                POSITION('@' IN SUBSTRING(p_instance_id FROM 2))
            );
            
            INSERT INTO $(SchemaName).Instances (
                TaskHub,
                InstanceID,
                ExecutionID,
                Name,
                Version,
                RuntimeStatus
            ) VALUES (
                v_task_hub,
                p_instance_id,
                gen_random_uuid()::TEXT,
                v_auto_start_orchestration_name,
                '',
                'Pending'
            );
        ELSE
            RAISE EXCEPTION 'The instance does not exist.';
        END IF;
    END IF;

    -- Store payload if provided
    IF p_payload_text IS NOT NULL THEN
        v_payload_id := gen_random_uuid();
        INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text)
        VALUES (v_task_hub, p_instance_id, v_payload_id, p_payload_text);
    END IF;

    -- Insert the event
    INSERT INTO $(SchemaName).NewEvents (
        Name,
        TaskHub,
        InstanceID,
        EventType,
        VisibleTime,
        PayloadID
    ) VALUES (
        p_name,
        v_task_hub,
        p_instance_id,
        'EventRaised',
        p_delivery_time,
        v_payload_id
    );
END;
$$;

-- Terminate an instance
CREATE OR REPLACE FUNCTION $(SchemaName).TerminateInstance(
    p_instance_id VARCHAR(100),
    p_reason TEXT DEFAULT NULL
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_existing_status VARCHAR(30);
    v_existing_lock_expiration TIMESTAMP WITH TIME ZONE;
    v_payload_id UUID;
    v_now TIMESTAMP WITH TIME ZONE;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();
    v_now := NOW() AT TIME ZONE 'UTC';

    -- Get the status of the existing orchestration
    SELECT I.RuntimeStatus, I.LockExpiration
    INTO v_existing_status, v_existing_lock_expiration
    FROM $(SchemaName).Instances I
    WHERE I.TaskHub = v_task_hub AND I.InstanceID = p_instance_id
    FOR UPDATE;

    IF v_existing_status IS NULL THEN
        RAISE EXCEPTION 'The instance does not exist.';
    END IF;

    IF v_existing_status IN ('Running', 'Pending') THEN
        -- Create a payload to store the reason, if any
        IF p_reason IS NOT NULL THEN
            v_payload_id := gen_random_uuid();
            INSERT INTO $(SchemaName).Payloads (TaskHub, InstanceID, PayloadID, Text)
            VALUES (v_task_hub, p_instance_id, v_payload_id, p_reason);
        END IF;

        -- Check if the orchestration hasn't started yet
        IF v_existing_status = 'Pending' AND (v_existing_lock_expiration IS NULL OR v_existing_lock_expiration <= v_now) THEN
            -- Transition directly to Terminated state
            UPDATE $(SchemaName).Instances SET
                RuntimeStatus = 'Terminated',
                LastUpdatedTime = v_now,
                CompletedTime = v_now,
                OutputPayloadID = v_payload_id,
                LockExpiration = NULL
            WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id;

            -- Delete pending messages
            DELETE FROM $(SchemaName).NewEvents 
            WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id;
        ELSE
            -- The orchestration has started, send a termination event
            IF NOT EXISTS (
                SELECT 1 FROM $(SchemaName).NewEvents
                WHERE TaskHub = v_task_hub AND InstanceID = p_instance_id AND EventType = 'ExecutionTerminated'
            ) THEN
                INSERT INTO $(SchemaName).NewEvents (
                    TaskHub,
                    InstanceID,
                    EventType,
                    PayloadID
                ) VALUES (
                    v_task_hub,
                    p_instance_id,
                    'ExecutionTerminated',
                    v_payload_id
                );
            END IF;
        END IF;
    END IF;
END;
$$;

-- Purge instance state by ID
CREATE OR REPLACE FUNCTION $(SchemaName).PurgeInstanceStateByID(
    p_instance_ids VARCHAR(100)[]
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_deleted_instances INTEGER;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    DELETE FROM $(SchemaName).NewEvents 
    WHERE TaskHub = v_task_hub AND InstanceID = ANY(p_instance_ids);
    
    DELETE FROM $(SchemaName).NewTasks  
    WHERE TaskHub = v_task_hub AND InstanceID = ANY(p_instance_ids);
    
    DELETE FROM $(SchemaName).Instances 
    WHERE TaskHub = v_task_hub AND InstanceID = ANY(p_instance_ids);
    
    GET DIAGNOSTICS v_deleted_instances = ROW_COUNT;
    
    DELETE FROM $(SchemaName).History  
    WHERE TaskHub = v_task_hub AND InstanceID = ANY(p_instance_ids);
    
    DELETE FROM $(SchemaName).Payloads 
    WHERE TaskHub = v_task_hub AND InstanceID = ANY(p_instance_ids);

    RETURN v_deleted_instances;
END;
$$;

-- Purge instance state by time
CREATE OR REPLACE FUNCTION $(SchemaName).PurgeInstanceStateByTime(
    p_threshold_time TIMESTAMP WITH TIME ZONE,
    p_filter_type SMALLINT DEFAULT 0
)
RETURNS INTEGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_hub VARCHAR(50);
    v_instance_ids VARCHAR(100)[];
    v_deleted_instances INTEGER;
BEGIN
    v_task_hub := $(SchemaName).CurrentTaskHub();

    IF p_filter_type = 0 THEN -- created time
        SELECT ARRAY_AGG(InstanceID)
        INTO v_instance_ids
        FROM $(SchemaName).Instances
        WHERE TaskHub = v_task_hub 
          AND RuntimeStatus IN ('Completed', 'Terminated', 'Failed')
          AND CreatedTime <= p_threshold_time;
    ELSIF p_filter_type = 1 THEN -- completed time
        SELECT ARRAY_AGG(InstanceID)
        INTO v_instance_ids
        FROM $(SchemaName).Instances
        WHERE TaskHub = v_task_hub 
          AND RuntimeStatus IN ('Completed', 'Terminated', 'Failed')
          AND CompletedTime <= p_threshold_time;
    ELSE
        RAISE EXCEPTION 'Unknown or unsupported filter type: %', p_filter_type;
    END IF;

    IF v_instance_ids IS NOT NULL THEN
        v_deleted_instances := $(SchemaName).PurgeInstanceStateByID(v_instance_ids);
    ELSE
        v_deleted_instances := 0;
    END IF;

    RETURN v_deleted_instances;
END;
$$;

-- Set global setting
CREATE OR REPLACE FUNCTION $(SchemaName).SetGlobalSetting(
    p_name VARCHAR(300),
    p_value TEXT
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_now TIMESTAMP WITH TIME ZONE;
BEGIN
    v_now := NOW() AT TIME ZONE 'UTC';

    INSERT INTO $(SchemaName).GlobalSettings (Name, Value, Timestamp, LastModifiedBy)
    VALUES (p_name, p_value, v_now, CURRENT_USER)
    ON CONFLICT (Name) DO UPDATE SET
        Value = EXCLUDED.Value,
        Timestamp = v_now,
        LastModifiedBy = CURRENT_USER;
END;
$$;

