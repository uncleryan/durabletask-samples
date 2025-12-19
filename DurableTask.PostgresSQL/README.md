# DurableTask.PostgresSQL - PostgreSQL Provider for Durable Task Framework

## Overview

This project is a PostgreSQL implementation of the Durable Task Framework, mirroring the structure and functionality of the `DurableTask.SqlServer` project. It provides a PostgreSQL-based storage backend for durable orchestrations and activities.

## Project Status

? **Core Infrastructure**: Complete  
? **Database Schema Scripts**: Complete  
? **PostgreSQL Functions**: Complete  
?? **Service Implementation**: Stub methods need implementation  
? **Testing**: Pending  

## Project Structure

### Core Files

#### Configuration
- **PostgresOrchestrationServiceSettings.cs** - Configuration settings for the PostgreSQL provider
  - Connection string management using `NpgsqlConnectionStringBuilder`
  - Task hub and schema name configuration
  - Polling intervals and concurrency settings
  - Database creation options

#### Main Service
- **PostgresOrchestrationService.cs** - Main orchestration service implementation
  - Implements `OrchestrationServiceBase` abstract class
  - Provides methods for orchestration and activity work item management
  - Placeholder methods for future implementation of database operations
  - Uses Npgsql for PostgreSQL connectivity

#### Database Management
- **PostgresDbManager.cs** - Handles database schema creation and management
  - Creates and upgrades database schema using versioned SQL scripts
  - Uses PostgreSQL advisory locks for synchronization
  - Supports embedded SQL script resources
  - Database and schema creation/deletion operations

#### Utilities
- **PostgresUtils.cs** - Helper methods for PostgreSQL operations
  - Database reader extensions
  - Retry logic for transient errors
  - PostgreSQL error code handling
  - Parameter helper methods

- **DTUtils.cs** - Utility functions for the Durable Task Framework
  - Version management
  - JSON serialization/deserialization
  - Extension version tracking

- **BackoffPollingHelper.cs** - Implements exponential backoff for polling
  - Randomized exponential backoff strategy
  - Async event coordination

#### Logging
- **LogHelper.cs** - Centralized logging helper
- **Logging/LogEvents.cs** - Structured log event definitions
- **Logging/EventIds.cs** - Logging event ID constants
- **Logging/DefaultEventSource.cs** - ETW event source for performance monitoring

### SQL Scripts (Complete!)

All PostgreSQL scripts have been created and are ready for deployment:

#### Schema Scripts
- **schema-1.0.0.sql** - Complete database schema
  - Tables: Versions, Payloads, Instances, NewEvents, History, NewTasks, GlobalSettings
  - Indexes for optimal query performance
  - Initial configuration data

#### Logic Scripts (Functions & Procedures)
- **logic-1.0.0.sql** - Core helper functions and views
  - `CurrentTaskHub()`, `GetScaleMetric()`, `GetScaleRecommendation()`
  - Views: `vInstances`, `vHistory`
  - `CreateInstance()` - Create orchestration instances
  - `_GetVersions()` - Version tracking

- **logic-1.0.0-part2.sql** - Instance management
  - `GetInstanceHistory()` - Retrieve execution history
  - `RaiseEvent()` - Send external events
  - `TerminateInstance()` - Force terminate orchestrations
  - `PurgeInstanceStateByID()`, `PurgeInstanceStateByTime()` - Cleanup
  - `SetGlobalSetting()` - Configuration management

- **logic-1.0.0-part3.sql** - Work item locking & checkpoints (Critical!)
  - `_LockNextOrchestration()` - Lock and fetch orchestration work items
  - `_CheckpointOrchestration()` - Save orchestration state

- **logic-1.0.0-part4.sql** - Activity tasks & queries
  - `_LockNextTask()` - Lock and fetch activity tasks
  - `_RenewOrchestrationLocks()`, `_RenewTaskLocks()` - Lock renewal
  - `_CompleteTasks()` - Complete activity tasks
  - `QuerySingleOrchestration()`, `_QueryManyOrchestrations()` - Queries
  - `_DiscardEventsAndUnlockInstance()` - Discard duplicates
  - `_AddOrchestrationEvents()` - Send messages

#### Utility Scripts
- **drop-schema.sql** - Clean schema removal

See [Scripts/README.md](Scripts/README.md) for detailed documentation.

## Key Differences from SQL Server Implementation

### Database Provider
- Uses `Npgsql` (9.0.2) instead of `Microsoft.Data.SqlClient`
- Uses `NpgsqlConnection`, `NpgsqlCommand`, etc.

### Data Types
- `VARCHAR` ? `VARCHAR`
- `varchar(MAX)` ? `TEXT`
- `datetime2` ? `TIMESTAMP WITH TIME ZONE`
- `uniqueidentifier` ? `UUID`
- `bit` ? `BOOLEAN`
- `IDENTITY` ? `BIGSERIAL`

### Locking Mechanisms
- **Application Level**: PostgreSQL advisory locks (`pg_advisory_lock`/`pg_advisory_unlock`)
- **Row Level**: `FOR UPDATE SKIP LOCKED` instead of `WITH (READPAST)`

### Stored Procedures ? Functions
- `CREATE OR ALTER PROCEDURE` ? `CREATE OR REPLACE FUNCTION`
- `OUTPUT` parameters ? `RETURNS TABLE` or `RETURNS VOID`
- Table-valued parameters ? `JSONB` arrays
- `NEWID()` ? `gen_random_uuid()`
- `SYSUTCDATETIME()` ? `NOW() AT TIME ZONE 'UTC'`

### Error Handling
- SQL Server error numbers ? PostgreSQL SQLState codes
- Adapted transient error detection for PostgreSQL

## NuGet Dependencies

- **Microsoft.Azure.DurableTask.Core** (3.6.1) - Core framework
- **Npgsql** (9.0.2) - PostgreSQL data provider
- **SemanticVersion** (2.1.0) - Version management
- **Azure.Core** (1.50.0) - Azure SDK core
- **Azure.Identity** (1.17.1) - Azure authentication
- **Microsoft.IdentityModel.JsonWebTokens** (8.15.0) - JWT support
- **System.IdentityModel.Tokens.Jwt** (8.15.0) - JWT tokens

## Setup Instructions

### 1. Database Setup

```sql
-- Create database
CREATE DATABASE durabletask;

-- Connect to database
\c durabletask

-- Run schema script
\i schema-1.0.0.sql

-- Run logic scripts
\i logic-1.0.0.sql
\i logic-1.0.0-part2.sql
\i logic-1.0.0-part3.sql
\i logic-1.0.0-part4.sql
```

### 2. Application Configuration

```csharp
var settings = new PostgresOrchestrationServiceSettings(
    connectionString: "Host=localhost;Database=durabletask;Username=postgres;Password=password",
    taskHubName: "MyTaskHub",
    schemaName: "dt");

var service = new PostgresOrchestrationService(settings);
await service.CreateIfNotExistsAsync();
await service.StartAsync();
```

## Next Steps - Implementation Work Required

### Priority 1: Core Service Methods

Implement the following methods in `PostgresOrchestrationService.cs` to call the PostgreSQL functions:

1. **LockNextTaskOrchestrationWorkItemAsync**
   - Call `_LockNextOrchestration()`
   - Parse result sets and construct `TaskOrchestrationWorkItem`

2. **CompleteTaskOrchestrationWorkItemAsync**
   - Convert events to JSONB format
   - Call `_CheckpointOrchestration()`

3. **LockNextTaskActivityWorkItem**
   - Call `_LockNextTask()`
   - Parse result and construct `TaskActivityWorkItem`

4. **CompleteTaskActivityWorkItemAsync**
   - Convert results to JSONB format
   - Call `_CompleteTasks()`

5. **CreateTaskOrchestrationAsync**
   - Call `CreateInstance()` function

6. **SendTaskOrchestrationMessageAsync**
   - Call `_AddOrchestrationEvents()` or `RaiseEvent()`

7. **GetOrchestrationStateAsync**
   - Call `QuerySingleOrchestration()`

8. **PurgeInstanceStateAsync**
   - Call `PurgeInstanceStateByID()` or `PurgeInstanceStateByTime()`

### Priority 2: Helper Classes

Create helper classes for data transformation:

```csharp
// Convert DurableTask events to JSONB format
public static class PostgresEventSerializer
{
    public static string ToJsonB(IEnumerable<TaskMessage> messages) { ... }
    public static string ToJsonB(IEnumerable<HistoryEvent> events) { ... }
}

// Parse PostgreSQL function results
public static class PostgresResultParser
{
    public static TaskOrchestrationWorkItem ParseOrchestrationWorkItem(NpgsqlDataReader reader) { ... }
    public static TaskActivityWorkItem ParseActivityWorkItem(NpgsqlDataReader reader) { ... }
}
```

### Priority 3: Testing

1. **Unit Tests**
   - Test each service method
   - Mock database connections
   - Test error handling

2. **Integration Tests**
   - Test against real PostgreSQL database
   - Test orchestration execution
   - Test activity execution
   - Test error recovery

3. **Performance Tests**
   - Benchmark against SQL Server implementation
   - Test under load
   - Identify bottlenecks

### Priority 4: Documentation

1. **API Documentation** - XML docs for public methods
2. **Migration Guide** - SQL Server to PostgreSQL
3. **Troubleshooting Guide** - Common issues and solutions

## PostgreSQL-Specific Features

### Advantages
- **Open Source**: No licensing costs
- **JSONB Support**: Native JSON storage and querying
- **LISTEN/NOTIFY**: Built-in pub/sub (potential future optimization)
- **Better Deadlock Detection**: Automatic deadlock detection and resolution
- **Partial Indexes**: For more efficient queries
- **Extensions**: Rich ecosystem (pgcrypto, pg_stat_statements, etc.)

### Considerations
- Different transaction isolation semantics
- Case-sensitive identifiers (if not quoted)
- Different connection pooling behavior
- Different backup/restore procedures

## Contributing

When implementing the remaining functionality:

1. Follow PostgreSQL best practices
2. Maintain compatibility with the Durable Task Framework interfaces
3. Include comprehensive error handling
4. Add structured logging at appropriate levels
5. Write unit and integration tests
6. Update documentation

## Example Usage (Once Completed)

```csharp
// Configure the service
var settings = new PostgresOrchestrationServiceSettings(
    connectionString: "Host=localhost;Database=durabletask;Username=postgres;Password=password",
    taskHubName: "MyTaskHub");

settings.MaxConcurrentActivities = 20;
settings.MaxActiveOrchestrations = 10;

// Create the service
var service = new PostgresOrchestrationService(settings);
await service.CreateIfNotExistsAsync();
await service.StartAsync();

// Use with Durable Task Framework
var worker = new TaskHubWorker(service);
worker.AddTaskOrchestrations(typeof(MyOrchestration));
worker.AddTaskActivities(typeof(MyActivity));
await worker.StartAsync();

// Create an instance
var client = new TaskHubClient(service);
var instance = await client.CreateOrchestrationInstanceAsync(
    typeof(MyOrchestration),
    input: "Hello, PostgreSQL!");

// Wait for completion
var result = await client.WaitForOrchestrationAsync(
    instance,
    TimeSpan.FromMinutes(5));
```

## License

Copyright (c) Microsoft Corporation.  
Licensed under the MIT License.

## Resources

- [Durable Task Framework](https://github.com/Azure/durabletask)
- [Npgsql Documentation](https://www.npgsql.org/doc/)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [Original SQL Server Implementation](../DurableTask.SqlServer/)
