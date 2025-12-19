# PostgreSQL Scripts

This directory contains SQL scripts for setting up the PostgreSQL schema for the Durable Task Framework.

## Available Scripts

### Schema Creation Scripts

#### `schema-1.0.0.sql`
The main schema definition script that creates:
- **Tables**: Versions, Payloads, Instances, NewEvents, History, NewTasks, GlobalSettings
- **Indexes**: Optimized indexes for query performance
- **Initial Data**: Default settings and version tracking

This script is idempotent and can be run multiple times safely.

### Logic Scripts (Functions and Procedures)

The logic is split across multiple files for maintainability:

#### `logic-1.0.0.sql` - Core Functions
- `CurrentTaskHub()` - Get the current task hub name based on configuration
- `GetScaleMetric()` - Get count of live instances and tasks
- `GetScaleRecommendation()` - Calculate recommended worker count
- Views: `vInstances`, `vHistory` - Queryable views with payload data
- `_GetVersions()` - Schema version tracking
- `CreateInstance()` - Create a new orchestration instance

#### `logic-1.0.0-part2.sql` - Instance Operations
- `GetInstanceHistory()` - Retrieve orchestration execution history
- `RaiseEvent()` - Send external events to orchestrations
- `TerminateInstance()` - Force terminate a running orchestration
- `PurgeInstanceStateByID()` - Clean up completed instances by ID
- `PurgeInstanceStateByTime()` - Clean up completed instances by time threshold
- `SetGlobalSetting()` - Update global configuration settings

#### `logic-1.0.0-part3.sql` - Work Item Locking & Checkpoints
- `_LockNextOrchestration()` - Lock and fetch the next orchestration work item
- `_CheckpointOrchestration()` - Save orchestration state and send new events/tasks

These are the most critical functions for the orchestration runtime engine.

#### `logic-1.0.0-part4.sql` - Activity Tasks & Queries
- `_LockNextTask()` - Lock and fetch the next activity task
- `_RenewOrchestrationLocks()` - Extend orchestration lock expiration
- `_RenewTaskLocks()` - Extend activity task lock expiration
- `_CompleteTasks()` - Complete activity tasks and send results
- `QuerySingleOrchestration()` - Query a single instance by ID
- `_QueryManyOrchestrations()` - Query multiple instances with filtering
- `_DiscardEventsAndUnlockInstance()` - Discard duplicate work items
- `_AddOrchestrationEvents()` - Send messages/events to orchestrations

### Utility Scripts

#### `drop-schema.sql`
Drops the entire schema and all objects using `CASCADE`. Use with caution!

## Script Naming Convention

Scripts follow the naming pattern: `schema-{version}.sql` and `logic-{version}.sql`

For example:
- `schema-1.0.0.sql` - Schema version 1.0.0
- `logic-1.0.0.sql` - Logic version 1.0.0

The version number is used to determine the order of script execution during schema creation and upgrades.

## PostgreSQL-Specific Features Used

### Data Types
- `VARCHAR` instead of SQL Server's `varchar`
- `TEXT` for large payloads instead of `varchar(MAX)`
- `TIMESTAMP WITH TIME ZONE` instead of SQL Server's `datetime2`
- `UUID` instead of SQL Server's `uniqueidentifier`
- `BOOLEAN` instead of SQL Server's `bit`
- `BIGSERIAL` for auto-incrementing sequence numbers

### Syntax Adaptations
- `CREATE OR REPLACE FUNCTION` instead of `CREATE OR ALTER PROCEDURE`
- `RETURNS TABLE` for result sets instead of output parameters
- `LANGUAGE plpgsql` for stored procedure language
- `RETURN QUERY` instead of `SELECT` for result sets
- `FOR UPDATE SKIP LOCKED` instead of `WITH (READPAST)`
- `ON CONFLICT ... DO NOTHING` instead of `IGNORE_DUP_KEY`
- `JSONB` for passing complex table-valued parameters
- `STRING_TO_ARRAY()` for parsing comma-separated values
- `gen_random_uuid()` instead of `NEWID()`
- `EXTRACT(EPOCH FROM ...)` for time calculations
- `NOW() AT TIME ZONE 'UTC'` instead of `SYSUTCDATETIME()`

### Locking & Concurrency
- PostgreSQL advisory locks in `PostgresDbManager.cs` (application-level)
- Row-level locking with `FOR UPDATE SKIP LOCKED` for work item selection
- Automatic MVCC (Multi-Version Concurrency Control) instead of SQL Server's locking hints

### Indexes
- PostgreSQL supports `INCLUDE` columns similar to SQL Server
- Partial indexes can be added for additional optimization (not yet used)

## Parameter Passing

Since PostgreSQL doesn't have table-valued parameters like SQL Server, we use JSONB to pass complex data structures:

```sql
-- Example: Passing deleted events
p_deleted_events JSONB -- [{"InstanceID": "xyz", "SequenceNumber": 123}, ...]

-- Example: Passing new history events
p_new_history_events JSONB -- Array of history event objects
```

This approach provides flexibility and is well-supported by Npgsql.

## Testing Considerations

1. **Schema Creation**: Run `schema-1.0.0.sql` to create the schema
2. **Logic Deployment**: Run all `logic-1.0.0*.sql` files in order
3. **Verification**: Check that all functions exist:
   ```sql
   SELECT routine_name, routine_type 
   FROM information_schema.routines 
   WHERE routine_schema = 'dt' 
   ORDER BY routine_name;
   ```

## Performance Notes

- Indexes are optimized for common query patterns
- `SKIP LOCKED` ensures non-blocking work item selection
- Payload data is stored separately to reduce row size
- History events for `TaskScheduled` and `SubOrchestrationInstanceCreated` don't load payload text

## Future Enhancements

Potential optimizations for future versions:

1. **LISTEN/NOTIFY**: PostgreSQL's pub/sub feature could reduce polling
2. **Partial Indexes**: For filtering specific runtime statuses
3. **Materialized Views**: For complex aggregations
4. **Partitioning**: For very large history tables
5. **Full-Text Search**: For searching instance data

## Migration from SQL Server

Key differences to be aware of when migrating:

1. Case sensitivity in PostgreSQL depends on configuration
2. Transaction isolation levels work differently
3. Error codes are different (SQLState vs error numbers)
4. Connection string format is different (Npgsql vs SqlClient)

## Troubleshooting

### Common Issues

1. **Schema Not Found**: Ensure $(SchemaName) placeholder is replaced at runtime
2. **Permission Errors**: Grant necessary permissions to the database user
3. **Lock Timeouts**: Adjust lock expiration times in application settings
4. **Deadlocks**: PostgreSQL has better deadlock detection than SQL Server

### Debugging

Enable query logging in PostgreSQL:
```sql
ALTER DATABASE yourdb SET log_statement = 'all';
```

Check active connections and locks:
```sql
SELECT * FROM pg_stat_activity WHERE datname = 'yourdb';
SELECT * FROM pg_locks WHERE database = (SELECT oid FROM pg_database WHERE datname = 'yourdb');
