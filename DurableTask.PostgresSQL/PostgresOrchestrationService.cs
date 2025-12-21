namespace DurableTask.PostgresSQL
{
    using DurableTask.Core;
    using DurableTask.Core.History;
    using DurableTask.Core.Query;
    using DurableTask.PostgresSQL.Utils;
    using Npgsql;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class PostgresOrchestrationService : OrchestrationServiceBase
    {
        readonly PostgresOrchestrationServiceSettings settings;
        readonly BackoffPollingHelper orchestrationBackoffHelper;
        readonly BackoffPollingHelper activityBackoffHelper;
        readonly LogHelper traceHelper;
        readonly PostgresDbManager dbManager;
        readonly string lockedByValue;
        readonly string userId;

        public PostgresOrchestrationService(PostgresOrchestrationServiceSettings? settings)
        {
            this.settings = ValidateSettings(settings) ?? throw new ArgumentNullException(nameof(settings));
            this.orchestrationBackoffHelper = new BackoffPollingHelper(
                this.settings.MinOrchestrationPollingInterval,
                this.settings.MaxOrchestrationPollingInterval,
                this.settings.DeltaBackoffOrchestrationPollingInterval);
            this.activityBackoffHelper = new BackoffPollingHelper(
                this.settings.MinActivityPollingInterval,
                this.settings.MaxActivityPollingInterval,
                this.settings.DeltaBackoffActivityPollingInterval);
            this.traceHelper = new LogHelper(this.settings.LoggerFactory.CreateLogger("DurableTask.PostgresSQL"));
            this.dbManager = new PostgresDbManager(this.settings, this.traceHelper);
            this.lockedByValue = $"{this.settings.AppName},{Process.GetCurrentProcess().Id}";
            this.userId = new NpgsqlConnectionStringBuilder(this.settings.TaskHubConnectionString).Username ?? string.Empty;
        }

        public override int MaxConcurrentTaskOrchestrationWorkItems => this.settings.MaxActiveOrchestrations;

        public override int MaxConcurrentTaskActivityWorkItems => this.settings.MaxConcurrentActivities;

        static PostgresOrchestrationServiceSettings? ValidateSettings(PostgresOrchestrationServiceSettings? settings)
        {
            if (settings != null)
            {
                if (string.IsNullOrEmpty(settings.TaskHubConnectionString))
                {
                    throw new ArgumentException($"A non-empty connection string value must be provided.", nameof(settings));
                }

                if (settings.WorkItemLockTimeout < TimeSpan.FromSeconds(10))
                {
                    throw new ArgumentException($"The {nameof(settings.WorkItemLockTimeout)} property value must be at least 10 seconds.", nameof(settings));
                }

                if (settings.WorkItemBatchSize < 10)
                {
                    throw new ArgumentException($"The {nameof(settings.WorkItemBatchSize)} property value must be at least 10.", nameof(settings));
                }
            }

            return settings;
        }

        async Task<NpgsqlConnection> GetAndOpenConnectionAsync(CancellationToken cancelToken = default)
        {
            if (cancelToken == default)
            {
                cancelToken = this.ShutdownToken;
            }

            NpgsqlConnection connection = this.settings.CreateConnection();
            await connection.OpenAsync(cancelToken);
            return connection;
        }

        NpgsqlCommand GetFunctionCommand(NpgsqlConnection connection, string functionName)
        {
            NpgsqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = functionName;
            return command;
        }

        public override Task CreateAsync(bool recreateInstanceStore)
            => this.dbManager.CreateOrUpgradeSchemaAsync(recreateInstanceStore);

        public override Task CreateIfNotExistsAsync()
            => this.CreateAsync(recreateInstanceStore: false);

        public override Task DeleteAsync()
            => this.DeleteAsync(deleteInstanceStore: false);

        public override Task DeleteAsync(bool deleteInstanceStore)
            => this.dbManager.DeleteSchemaAsync(deleteInstanceStore);

        public override Task StartAsync()
        {
            // No specific startup logic needed for PostgreSQL
            return Task.CompletedTask;
        }

        public override Task StopAsync(bool isForced)
        {
            // No specific shutdown logic needed
            return Task.CompletedTask;
        }

        public override async Task<bool> IsTaskHubReady(CancellationToken cancellationToken)
        {
            try
            {
                await using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync(cancellationToken);
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = '{this.settings.SchemaName}')";
                object? result = await command.ExecuteScalarAsync(cancellationToken);
                return result is bool exists && exists;
            }
            catch
            {
                return false;
            }
        }

        public override int GetDelayInSecondsAfterOnProcessException(Exception exception)
        {
            return exception is OperationCanceledException ? 0 : 10;
        }

        public override int GetDelayInSecondsAfterOnFetchException(Exception exception)
        {
            return exception is OperationCanceledException ? 0 : 10;
        }

        // ========================================
        // IMPLEMENTED METHODS
        // ========================================

        public override async Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[] dedupeStatuses)
        {
            var startEvent = creationMessage.Event as ExecutionStartedEvent
                ?? throw new ArgumentException("Message must contain ExecutionStartedEvent", nameof(creationMessage));

            using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
            using NpgsqlCommand command = this.GetFunctionCommand(connection, $"{this.settings.SchemaName}.CreateInstance");

            command.Parameters.AddWithValue("p_name", startEvent.Name ?? string.Empty);
            command.Parameters.AddWithValue("p_version", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)startEvent.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("p_instance_id", creationMessage.OrchestrationInstance.InstanceId);
            command.Parameters.AddWithValue("p_execution_id", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)creationMessage.OrchestrationInstance.ExecutionId ?? DBNull.Value);
            command.Parameters.AddWithValue("p_input_text", NpgsqlTypes.NpgsqlDbType.Text, (object?)startEvent.Input ?? DBNull.Value);
            command.Parameters.AddWithValue("p_start_time", NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)startEvent.ScheduledStartTime ?? DBNull.Value);
            command.Parameters.AddWithValue("p_dedupe_statuses", string.Join(",", dedupeStatuses.Select(s => s.ToString())));
            
            // Handle trace context - convert to string if available
            string? traceContext = null;
            if (startEvent.ParentTraceContext != null)
            {
                traceContext = $"{startEvent.ParentTraceContext.SpanId:X16}";
            }
            command.Parameters.AddWithValue("p_trace_context", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)traceContext ?? DBNull.Value);

            await PostgresUtils.ExecuteNonQueryAsync(command, this.traceHelper, creationMessage.OrchestrationInstance.InstanceId);
        }

        public override Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
        {
            return this.CreateTaskOrchestrationAsync(creationMessage, Array.Empty<OrchestrationStatus>());
        }

        public override async Task<OrchestrationState?> GetOrchestrationStateAsync(string instanceId, string? executionId)
        {
            using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
            using NpgsqlCommand command = this.GetFunctionCommand(connection, $"{this.settings.SchemaName}.QuerySingleOrchestration");

            command.Parameters.AddWithValue("p_instance_id", instanceId);
            command.Parameters.AddWithValue("p_execution_id", NpgsqlTypes.NpgsqlDbType.Varchar, (object?)executionId ?? DBNull.Value);
            command.Parameters.AddWithValue("p_fetch_input", false);
            command.Parameters.AddWithValue("p_fetch_output", true);

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
                Version = reader.IsDBNull(reader.GetOrdinal("Version")) ? null : reader.GetString(reader.GetOrdinal("Version")),
                OrchestrationStatus = Enum.Parse<OrchestrationStatus>(reader.GetString(reader.GetOrdinal("RuntimeStatus"))),
                CreatedTime = reader.GetDateTime(reader.GetOrdinal("CreatedTime")),
                LastUpdatedTime = reader.GetDateTime(reader.GetOrdinal("LastUpdatedTime")),
                CompletedTime = reader.IsDBNull(reader.GetOrdinal("CompletedTime")) 
                    ? DateTime.MinValue 
                    : reader.GetDateTime(reader.GetOrdinal("CompletedTime")),
                Status = reader.IsDBNull(reader.GetOrdinal("CustomStatusText")) 
                    ? null 
                    : reader.GetString(reader.GetOrdinal("CustomStatusText")),
                Output = reader.IsDBNull(reader.GetOrdinal("OutputText")) 
                    ? null 
                    : reader.GetString(reader.GetOrdinal("OutputText"))
            };
        }

        public override async Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
        {
            var state = await this.GetOrchestrationStateAsync(instanceId, executionId: null);
            return state != null ? new[] { state } : Array.Empty<OrchestrationState>();
        }

        public override async Task ForceTerminateTaskOrchestrationAsync(string instanceId, string reason)
        {
            using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
            using NpgsqlCommand command = this.GetFunctionCommand(connection, $"{this.settings.SchemaName}.TerminateInstance");

            command.Parameters.AddWithValue("p_instance_id", instanceId);
            command.Parameters.AddWithValue("p_reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);

            await PostgresUtils.ExecuteNonQueryAsync(command, this.traceHelper, instanceId);
        }

        public override async Task<PurgeResult> PurgeInstanceStateAsync(string instanceId)
        {
            using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
            using NpgsqlCommand command = this.GetFunctionCommand(connection, $"{this.settings.SchemaName}.PurgeInstanceStateByID");

            // Create array parameter
            var instanceIds = new[] { instanceId };
            command.Parameters.AddWithValue("p_instance_ids", instanceIds);

            var result = await PostgresUtils.ExecuteScalarAsync(command, this.traceHelper, instanceId);
            int deletedCount = result != null ? Convert.ToInt32(result) : 0;

            return new PurgeResult(deletedCount);
        }

        public override async Task<PurgeResult> PurgeInstanceStateAsync(PurgeInstanceFilter purgeInstanceFilter)
        {
            using NpgsqlConnection connection = await this.GetAndOpenConnectionAsync();
            using NpgsqlCommand command = this.GetFunctionCommand(connection, $"{this.settings.SchemaName}.PurgeInstanceStateByTime");

            // Use CreatedTimeTo as threshold (completed time filtering)
            int filterType = 1; // CompletedTime filter
            DateTime thresholdTime = purgeInstanceFilter.CreatedTimeTo ?? DateTime.UtcNow;

            command.Parameters.AddWithValue("p_threshold_time", thresholdTime);
            command.Parameters.AddWithValue("p_filter_type", filterType);

            var result = await PostgresUtils.ExecuteScalarAsync(command, this.traceHelper, null);
            int deletedCount = result != null ? Convert.ToInt32(result) : 0;

            return new PurgeResult(deletedCount);
        }

        public override Task PurgeOrchestrationHistoryAsync(DateTime thresholdDateTimeUtc, OrchestrationStateTimeRangeFilterType timeRangeFilterType)
        {
            var filter = new PurgeInstanceFilter(
                createdTimeFrom: DateTime.MinValue,
                createdTimeTo: thresholdDateTimeUtc,
                runtimeStatus: new[] { OrchestrationStatus.Completed, OrchestrationStatus.Failed, OrchestrationStatus.Terminated });

            return this.PurgeInstanceStateAsync(filter);
        }

        // ========================================
        // TODO: COMPLEX METHODS NEED IMPLEMENTATION
        // ========================================
        // These methods require complex JSONB serialization and multi-result-set handling
        // See IMPLEMENTATION_GUIDE.md for detailed implementation instructions

        public override Task<TaskOrchestrationWorkItem?> LockNextTaskOrchestrationWorkItemAsync(
            TimeSpan receiveTimeout,
            CancellationToken cancellationToken)
        {
            // TODO: Implement - See IMPLEMENTATION_GUIDE.md section "LockNextTaskOrchestrationWorkItemAsync"
            // This requires:
            // 1. Call _LockNextOrchestration() function
            // 2. Parse 3 result sets (messages, instance info, history)
            // 3. Build OrchestrationRuntimeState
            // 4. Return ExtendedOrchestrationWorkItem
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task CompleteTaskOrchestrationWorkItemAsync(
            TaskOrchestrationWorkItem workItem,
            OrchestrationRuntimeState newRuntimeState,
            IList<TaskMessage> outboundMessages,
            IList<TaskMessage> orchestratorMessages,
            IList<TaskMessage> timerMessages,
            TaskMessage continuedAsNewMessage,
            OrchestrationState state)
        {
            // TODO: Implement - See IMPLEMENTATION_GUIDE.md section "CompleteTaskOrchestrationWorkItemAsync"
            // This requires:
            // 1. Serialize events to JSONB
            // 2. Call _CheckpointOrchestration() function
            // 3. Handle duplicate execution detection
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task<TaskActivityWorkItem?> LockNextTaskActivityWorkItem(
            TimeSpan receiveTimeout,
            CancellationToken cancellationToken)
        {
            // TODO: Implement - Call _LockNextTask() function
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task<TaskActivityWorkItem?> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
        {
            // TODO: Implement - Call _RenewTaskLocks() function
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
        {
            // TODO: Set lock expiration to now to release the lock
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
        {
            // TODO: Implement - Call _CompleteTasks() function with JSONB results
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
        {
            // TODO: Implement - Call _RenewOrchestrationLocks() function
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
        {
            // TODO: Implement - Call _DiscardEventsAndUnlockInstance() function
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task SendTaskOrchestrationMessageAsync(TaskMessage message)
        {
            // TODO: Implement - Call RaiseEvent() or _AddOrchestrationEvents() function
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
        {
            // TODO: Implement - Call _AddOrchestrationEvents() with JSONB batch
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task<OrchestrationState?> WaitForOrchestrationAsync(
            string instanceId,
            string? executionId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // TODO: Implement - Poll GetOrchestrationStateAsync until complete or timeout
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task<string> GetOrchestrationHistoryAsync(string instanceId, string? executionId)
        {
            // TODO: Implement - Call GetInstanceHistory() and serialize to JSON
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }

        public override Task<OrchestrationQueryResult> GetOrchestrationWithQueryAsync(
            OrchestrationQuery query,
            CancellationToken cancellationToken)
        {
            // TODO: Implement - Call _QueryManyOrchestrations() function
            throw new NotImplementedException("See IMPLEMENTATION_GUIDE.md for implementation details");
        }
    }
}
