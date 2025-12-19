-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- PERSISTENT SCHEMA OBJECTS (tables, indexes, etc.)
--
-- The contents of this file must never be changed after
-- being published. Any schema changes must be done in
-- new schema-{major}.{minor}.{patch}.sql scripts.

-- All objects must be created under the "dt" schema or under a custom schema.
-- Note: $(SchemaName) will be replaced at runtime with the actual schema name

-- Create schema if it doesn't exist
CREATE SCHEMA IF NOT EXISTS $(SchemaName);

-- Create tables

-- Rule #1: Use VARCHAR instead of TEXT where size is known
-- Rule #2: Use TEXT for large payloads
-- Rule #3: Try to follow existing naming and ordering conventions

-- Versions table
CREATE TABLE IF NOT EXISTS $(SchemaName).Versions (
    SemanticVersion VARCHAR(100) NOT NULL PRIMARY KEY,
    UpgradeTime TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

-- Payloads table - stores orchestration inputs, outputs, and custom status
CREATE TABLE IF NOT EXISTS $(SchemaName).Payloads (
    TaskHub VARCHAR(50) NOT NULL,
    InstanceID VARCHAR(100) NOT NULL,
    PayloadID UUID NOT NULL,
    Text TEXT NULL,
    Reason TEXT NULL,
    -- NOTE: no FK constraint to Instances table because we want to allow events to create new instances
    
    CONSTRAINT PK_Payloads PRIMARY KEY (TaskHub, InstanceID, PayloadID)
);

-- Instances table - stores orchestration instance metadata
CREATE TABLE IF NOT EXISTS $(SchemaName).Instances (
    TaskHub VARCHAR(50) NOT NULL,
    InstanceID VARCHAR(100) NOT NULL,
    ExecutionID VARCHAR(50) NOT NULL DEFAULT gen_random_uuid()::TEXT,
    Name VARCHAR(300) NOT NULL, -- the type name of the orchestration or entity
    Version VARCHAR(100) NULL, -- the version of the orchestration (optional)
    CreatedTime TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    LastUpdatedTime TIMESTAMP WITH TIME ZONE NULL,
    CompletedTime TIMESTAMP WITH TIME ZONE NULL,
    RuntimeStatus VARCHAR(20) NOT NULL,
    LockedBy VARCHAR(100) NULL,
    LockExpiration TIMESTAMP WITH TIME ZONE NULL,
    InputPayloadID UUID NULL,
    OutputPayloadID UUID NULL,
    CustomStatusPayloadID UUID NULL,
    ParentInstanceID VARCHAR(100) NULL,
    TraceContext VARCHAR(800) NULL,
    
    CONSTRAINT PK_Instances PRIMARY KEY (TaskHub, InstanceID)
    -- NOTE: No FK constraints for the Payloads table because of high performance cost and deadlock risk
);

-- Index used by LockNext and Purge logic
CREATE INDEX IF NOT EXISTS IX_Instances_RuntimeStatus 
    ON $(SchemaName).Instances(TaskHub, RuntimeStatus)
    INCLUDE (LockExpiration, CreatedTime, CompletedTime);

-- Index to help the performance of multi-instance query
CREATE INDEX IF NOT EXISTS IX_Instances_CreatedTime 
    ON $(SchemaName).Instances(TaskHub, CreatedTime)
    INCLUDE (RuntimeStatus, CompletedTime, InstanceID);

-- NewEvents table - pending orchestration events/messages
CREATE TABLE IF NOT EXISTS $(SchemaName).NewEvents (
    SequenceNumber BIGSERIAL NOT NULL, -- order is important for FIFO
    Timestamp TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    VisibleTime TIMESTAMP WITH TIME ZONE NULL, -- for scheduled messages
    DequeueCount INT NOT NULL DEFAULT 0,
    TaskHub VARCHAR(50) NOT NULL,
    InstanceID VARCHAR(100) NOT NULL,
    ExecutionID VARCHAR(50) NULL,
    EventType VARCHAR(40) NOT NULL,
    RuntimeStatus VARCHAR(30) NULL,
    Name VARCHAR(300) NULL,
    TaskID INT NULL,
    PayloadID UUID NULL,
    TraceContext VARCHAR(800) NULL,
    
    CONSTRAINT PK_NewEvents PRIMARY KEY (TaskHub, InstanceID, SequenceNumber)
    -- NOTE: no FK constraint to Instances and Payloads tables because of high performance cost and deadlock risk.
    --       Also, we want to allow events to create new instances, which means an Instances row might not yet exist.
);

-- History table - orchestration execution history
CREATE TABLE IF NOT EXISTS $(SchemaName).History (
    TaskHub VARCHAR(50) NOT NULL,
    InstanceID VARCHAR(100) NOT NULL,
    ExecutionID VARCHAR(50) NOT NULL,
    SequenceNumber BIGINT NOT NULL,
    EventType VARCHAR(40) NOT NULL,
    TaskID INT NULL,
    Timestamp TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    IsPlayed BOOLEAN NOT NULL DEFAULT FALSE,
    Name VARCHAR(300) NULL,
    RuntimeStatus VARCHAR(20) NULL,
    VisibleTime TIMESTAMP WITH TIME ZONE NULL,
    DataPayloadID UUID NULL,
    TraceContext VARCHAR(800) NULL,
    
    CONSTRAINT PK_History PRIMARY KEY (TaskHub, InstanceID, ExecutionID, SequenceNumber)
    -- NOTE: no FK constraint to Payloads or Instances tables because of high performance cost and deadlock risk
);

-- NewTasks table - pending activity tasks
CREATE TABLE IF NOT EXISTS $(SchemaName).NewTasks (
    TaskHub VARCHAR(50) NOT NULL,
    SequenceNumber BIGSERIAL NOT NULL,  -- order is important for FIFO
    InstanceID VARCHAR(100) NOT NULL,
    ExecutionID VARCHAR(50) NULL,
    Name VARCHAR(300) NULL,
    TaskID INT NOT NULL,
    Timestamp TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    VisibleTime TIMESTAMP WITH TIME ZONE NULL,
    DequeueCount INT NOT NULL DEFAULT 0,
    LockedBy VARCHAR(100) NULL,
    LockExpiration TIMESTAMP WITH TIME ZONE NULL,
    PayloadID UUID NULL,
    Version VARCHAR(100) NULL,
    TraceContext VARCHAR(800) NULL,
    
    CONSTRAINT PK_NewTasks PRIMARY KEY (TaskHub, SequenceNumber)
    -- NOTE: no FK constraint to Payloads or Instances tables because of high performance cost and deadlock risk
);

-- Index used by scale hints
CREATE INDEX IF NOT EXISTS IX_NewTasks_InstanceID 
    ON $(SchemaName).NewTasks(TaskHub, InstanceID)
    INCLUDE (SequenceNumber, Timestamp, LockExpiration, VisibleTime);

-- GlobalSettings table - global configuration settings
CREATE TABLE IF NOT EXISTS $(SchemaName).GlobalSettings (
    Name VARCHAR(300) NOT NULL PRIMARY KEY,
    Value TEXT NULL,
    Timestamp TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    LastModifiedBy VARCHAR(128) NOT NULL DEFAULT CURRENT_USER
);

-- Default task hub mode is 1, or "User Name"
INSERT INTO $(SchemaName).GlobalSettings (Name, Value) 
VALUES ('TaskHubMode', '1')
ON CONFLICT (Name) DO NOTHING;

-- Insert the schema version
INSERT INTO $(SchemaName).Versions (SemanticVersion) 
VALUES ('1.0.0')
ON CONFLICT (SemanticVersion) DO NOTHING;
