# Implementation Guide for PostgresOrchestrationService

## Overview

This document provides a detailed guide for implementing the remaining methods in `PostgresOrchestrationService.cs`. The implementation involves calling PostgreSQL functions and transforming data between C# objects and JSONB format.

## Implementation Status

### ? Complete
- Project structure and configuration
- Database schema and all PostgreSQL functions
- Helper classes (LogHelper, PostgresUtils, DTUtils, PostgresDbManager)
- EventPayloadMap and ExtendedOrchestrationWorkItem

### ? TODO - Critical Methods (Priority 1)

1. **LockNextTaskOrchestrationWorkItemAsync** - Get orchestration work items
2. **CompleteTaskOrchestrationWorkItemAsync** - Save orchestration state
3. **LockNextTaskActivityWorkItem** - Get activity work items
4. **CompleteTaskActivityWorkItemAsync** - Complete activities
5. **CreateTaskOrchestrationAsync** - Create new instances

### ? TODO - Essential Methods (Priority 2)

6. **RenewTaskOrchestrationWorkItemLockAsync** - Extend orchestration locks
7. **RenewTaskActivityWorkItemLockAsync** - Extend activity locks
8. **AbandonTaskOrchestrationWorkItemAsync** - Release orchestration locks
9. **AbandonTaskActivityWorkItemAsync** - Release activity locks
10. **SendTaskOrchestrationMessageAsync** - Send events to orchestrations
11. **ForceTerminateTaskOrchestrationAsync** - Terminate orchestrations

### ? TODO - Query Methods (Priority 3)

12. **GetOrchestrationStateAsync** (single) - Query single instance
13. **GetOrchestrationStateAsync** (multiple) - Query multiple instances
14. **GetOrchestrationHistoryAsync** - Get full history
15. **GetOrchestrationWithQueryAsync** - Complex queries
16. **WaitForOrchestrationAsync** - Wait for completion

### ? TODO - Purge Methods (Priority 4)

17. **PurgeInstanceStateAsync** (by ID) - Delete single instance
18. **PurgeInstanceStateAsync** (by filter) - Delete multiple instances
19. **PurgeOrchestrationHistoryAsync** - Bulk delete by time

## Implementation Pattern

### General Pattern for Calling PostgreSQL Functions

```csharp
using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync(cancellationToken);
using NpgsqlCommand command = this.GetFunctionCommand(connection, $"{this.settings.SchemaName}.FunctionName");

// Add parameters
command.Parameters.AddWithValue("p_parameter_name", value);

// For JSONB parameters
var jsonbData = System.Text.Json.JsonSerializer.Serialize(dataObject);
command.Parameters.AddWithValue("p_jsonb_param", NpgsqlTypes.NpgsqlDbType.Jsonb, jsonbData);

// Execute and read results
using var reader = await PostgresUtils.ExecuteReaderAsync(command, this.traceHelper, instanceId);
while (await reader.ReadAsync(cancellationToken))
{
    // Parse results
}
```

## Detailed Implementation Examples

### Example 1: CreateTaskOrchestrationAsync

```csharp
public override async Task CreateTaskOrchestrationAsync(
    TaskMessage creationMessage,
    OrchestrationStatus[] dedupeStatuses)
{
    var startEvent = creationMessage.Event as ExecutionStartedEvent
        ?? throw new ArgumentException("Must be ExecutionStartedEvent", nameof(creationMessage));

    using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
    using NpgsqlCommand command = this.GetFunctionCommand(
        connection,
        $"{this.settings.SchemaName}.CreateInstance");

    command.Parameters.AddWithValue("p_name", startEvent.Name ?? string.Empty);
    command.Parameters.AddWithValue("p_version", (object?)startEvent.Version ?? DBNull.Value);
    command.Parameters.AddWithValue("p_instance_id", creationMessage.OrchestrationInstance.InstanceId);
    command.Parameters.AddWithValue("p_execution_id", (object?)creationMessage.OrchestrationInstance.ExecutionId ?? DBNull.Value);
    command.Parameters.AddWithValue("p_input_text", (object?)startEvent.Input ?? DBNull.Value);
    command.Parameters.AddWithValue("p_start_time", (object?)startEvent.ScheduledStartTime ?? DBNull.Value);
    command.Parameters.AddWithValue("p_dedupe_statuses", string.Join(",", dedupeStatuses));
    command.Parameters.AddWithValue("p_trace_context", (object?)startEvent.ParentTraceContext?.SerializeToString() ?? DBNull.Value);

    await PostgresUtils.ExecuteNonQueryAsync(
        command,
        this.traceHelper,
        creationMessage.OrchestrationInstance.InstanceId);
}
```

### Example 2: GetOrchestrationStateAsync (Single Instance)

```csharp
public override async Task<OrchestrationState?> GetOrchestrationStateAsync(
    string instanceId,
    string? executionId)
{
    using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
    using NpgsqlCommand command = this.GetFunctionCommand(
        connection,
        $"{this.settings.SchemaName}.QuerySingleOrchestration");

    command.Parameters.AddWithValue("p_instance_id", instanceId);
    command.Parameters.AddWithValue("p_execution_id", (object?)executionId ?? DBNull.Value);
    command.Parameters.AddWithValue("p_fetch_input", false);
    command.Parameters.AddWithValue("p_fetch_output", false);

    using var reader = await PostgresUtils.ExecuteReaderAsync(command, this.traceHelper, instanceId);
    
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new OrchestrationState
    {
        OrchestrationInstance = new OrchestrationInstance
        {
            InstanceId = reader.GetString(reader.GetOrdinal("InstanceID")),
            ExecutionId = reader.GetString(reader.GetOrdinal("ExecutionID"))
        },
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Version = reader.GetString(reader.GetOrdinal("Version")),
        OrchestrationStatus = Enum.Parse<OrchestrationStatus>(
            reader.GetString(reader.GetOrdinal("RuntimeStatus"))),
        CreatedTime = reader.GetDateTime(reader.GetOrdinal("CreatedTime")),
        LastUpdatedTime = reader.GetDateTime(reader.GetOrdinal("LastUpdatedTime")),
        CompletedTime = reader.IsDBNull(reader.GetOrdinal("CompletedTime"))
            ? DateTime.MinValue
            : reader.GetDateTime(reader.GetOrdinal("CompletedTime")),
        Status = reader.IsDBNull(reader.GetOrdinal("CustomStatusText"))
            ? null
            : reader.GetString(reader.GetOrdinal("CustomStatusText"))
    };
}
```

### Example 3: ForceTerminateTaskOrchestrationAsync

```csharp
public override async Task ForceTerminateTaskOrchestrationAsync(string instanceId, string reason)
{
    using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
    using NpgsqlCommand command = this.GetFunctionCommand(
        connection,
        $"{this.settings.SchemaName}.TerminateInstance");

    command.Parameters.AddWithValue("p_instance_id", instanceId);
    command.Parameters.AddWithValue("p_reason", (object?)reason ?? DBNull.Value);

    await PostgresUtils.ExecuteNonQueryAsync(command, this.traceHelper, instanceId);
}
```

## Complex Implementations

### LockNextTaskOrchestrationWorkItemAsync

This is the most complex method. It needs to:
1. Call `_LockNextOrchestration()` function
2. Parse 3 result sets (events, instance info, history)
3. Build `TaskOrchestrationWorkItem` with all data
4. Handle polling with backoff

**Key steps:**
- Result Set 1: New events (messages)
- Result Set 2: Instance status
- Result Set 3: Full history
- Build `OrchestrationRuntimeState` from history
- Return `ExtendedOrchestrationWorkItem`

### CompleteTaskOrchestrationWorkItemAsync

This method needs to:
1. Convert C# objects to JSONB format
2. Call `_CheckpointOrchestration()` function
3. Handle duplicate execution detection

**JSONB Serialization Required For:**
- Deleted events: `{InstanceID, SequenceNumber}[]`
- New history events: Full event objects
- New orchestration events: Event messages
- New task events: Activity task messages

## Helper Classes Needed

### PostgresEventSerializer

```csharp
internal static class PostgresEventSerializer
{
    public static string SerializeHistoryEvents(
        IList<HistoryEvent> events,
        OrchestrationInstance instance,
        int startSequenceNumber,
        EventPayloadMap payloadMap)
    {
        var eventObjects = new List<object>();
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            eventObjects.Add(new
            {
                InstanceID = instance.InstanceId,
                ExecutionID = instance.ExecutionId,
                SequenceNumber = startSequenceNumber + i,
                EventType = e.EventType.ToString(),
                TaskID = DTUtils.GetTaskEventId(e),
                Timestamp = e.Timestamp,
                IsPlayed = e.IsPlayed,
                Name = GetEventName(e),
                RuntimeStatus = GetRuntimeStatus(e),
                VisibleTime = GetVisibleTime(e),
                PayloadID = payloadMap.TryGetPayloadId(e, out Guid payloadId) ? payloadId : (Guid?)null,
                PayloadText = GetPayloadText(e),
                Reason = GetReason(e),
                TraceContext = GetTraceContext(e)
            });
        }
        return System.Text.Json.JsonSerializer.Serialize(eventObjects);
    }

    public static string SerializeMessages(IList<TaskMessage> messages)
    {
        var messageObjects = messages.Select(m => new
        {
            InstanceID = m.OrchestrationInstance.InstanceId,
            SequenceNumber = m.SequenceNumber
        });
        return System.Text.Json.JsonSerializer.Serialize(messageObjects);
    }

    // Additional helper methods for getting event properties
    private static string? GetEventName(HistoryEvent e) { /* ... */ }
    private static string? GetRuntimeStatus(HistoryEvent e) { /* ... */ }
    private static DateTime? GetVisibleTime(HistoryEvent e) { /* ... */ }
    private static string? GetPayloadText(HistoryEvent e) { /* ... */ }
    private static string? GetReason(HistoryEvent e) { /* ... */ }
    private static string? GetTraceContext(HistoryEvent e) { /* ... */ }
}
```

### PostgresResultParser

```csharp
internal static class PostgresResultParser
{
    public static TaskMessage ReadTaskMessage(NpgsqlDataReader reader)
    {
        var eventType = Enum.Parse<EventType>(reader.GetString("EventType"));
        HistoryEvent historyEvent = CreateHistoryEvent(eventType, reader);
        
        return new TaskMessage
        {
            SequenceNumber = reader.GetInt64("SequenceNumber"),
            Event = historyEvent,
            OrchestrationInstance = new OrchestrationInstance
            {
                InstanceId = reader.GetString("InstanceID"),
                ExecutionId = reader.GetString("ExecutionID")
            }
        };
    }

    public static IList<HistoryEvent> ReadHistoryEvents(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var events = new List<HistoryEvent>();
        while (reader.Read())
        {
            var eventType = Enum.Parse<EventType>(reader.GetString("EventType"));
            HistoryEvent historyEvent = CreateHistoryEvent(eventType, reader);
            events.Add(historyEvent);
        }
        return events;
    }

    private static HistoryEvent CreateHistoryEvent(EventType eventType, NpgsqlDataReader reader)
    {
        // Complex switch statement to create appropriate event type
        // Each event type has different properties to map
        switch (eventType)
        {
            case EventType.ExecutionStarted:
                return new ExecutionStartedEvent(
                    reader.GetInt32("TaskID"),
                    reader.GetString("PayloadText"))
                {
                    Name = reader.GetString("Name"),
                    // Map other properties...
                };
            // ... handle all other event types
            default:
                throw new NotSupportedException($"Event type {eventType} not supported");
        }
    }
}
```

## Testing Strategy

### Unit Tests
1. Test each method in isolation with mocked database
2. Test JSONB serialization/deserialization
3. Test error handling

### Integration Tests
1. Deploy PostgreSQL schema
2. Test create ? lock ? complete flow
3. Test activity execution
4. Test queries and purge operations
5. Test error recovery and retries

## Performance Considerations

1. **Connection Pooling**: Already configured in Npgsql
2. **Batch Operations**: Use JSONB arrays for bulk operations
3. **Indexes**: Already created in schema
4. **Query Optimization**: Use EXPLAIN ANALYZE in PostgreSQL

## Common Pitfalls

1. **JSONB Serialization**: Use `System.Text.Json`, not `Newtonsoft.Json` for JSONB
2. **NULL Handling**: Use `DBNull.Value` for SQL NULL, check `IsDBNull()` when reading
3. **Time Zones**: Always use UTC, PostgreSQL functions use `TIMESTAMP WITH TIME ZONE`
4. **Error Codes**: PostgreSQL uses SQLState (e.g., "23505" for unique violation), not error numbers
5. **Parameter Names**: PostgreSQL uses positional parameters ($1, $2) or named (@param)

## Recommended Implementation Order

1. **Phase 1** (Makes service minimally functional):
   - CreateTaskOrchestrationAsync
   - LockNextTaskOrchestrationWorkItemAsync
   - CompleteTaskOrchestrationWorkItemAsync
   - GetOrchestrationStateAsync (single)

2. **Phase 2** (Adds activity support):
   - LockNextTaskActivityWorkItem
   - CompleteTaskActivityWorkItemAsync
   - RenewTaskOrchestrationWorkItemLockAsync
   - RenewTaskActivityWorkItemLockAsync

3. **Phase 3** (Adds management features):
   - ForceTerminateTaskOrchestrationAsync
   - SendTaskOrchestrationMessageAsync
   - GetOrchestrationHistoryAsync
   - Query methods

4. **Phase 4** (Adds cleanup):
   - Purge methods
   - Abandon methods

## Next Steps

1. Create `PostgresEventSerializer.cs` with JSONB serialization
2. Create `PostgresResultParser.cs` with result parsing
3. Implement Phase 1 methods one by one
4. Write integration tests for each phase
5. Performance testing and optimization

## Reference Files

- SQL Server Implementation: `DurableTask.SqlServer\SqlOrchestrationService.cs`
- PostgreSQL Functions: `DurableTask.PostgresSQL\Scripts\logic-1.0.0*.sql`
- Helper Utilities: `DurableTask.SqlServer\SqlUtils.cs`
- Event Types: `DurableTask.SqlServer\SqlTypes\*.cs`

## Estimated Effort

- Phase 1: 2-3 days
- Phase 2: 1-2 days
- Phase 3: 2-3 days
- Phase 4: 1 day
- Testing: 2-3 days
- **Total: ~10-15 days** for complete implementation

---

This implementation guide should help you complete the PostgresOrchestrationService implementation systematically. The complexity is mainly in data transformation between C# objects and JSONB format, and in parsing the multi-result-set responses from PostgreSQL functions.
