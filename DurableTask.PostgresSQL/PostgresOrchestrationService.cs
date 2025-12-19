namespace DurableTask.PostgresSQL
{
    using DurableTask.Core;
    using DurableTask.Core.Query;
    using DurableTask.PostgresSQL.Utils;
    using Npgsql;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
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
            // TODO: Implement startup logic
            return Task.CompletedTask;
        }

        public override Task StopAsync(bool isForced)
        {
            // TODO: Implement shutdown logic
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
            // TODO: Implement exception handling logic
            return 10;
        }

        public override int GetDelayInSecondsAfterOnFetchException(Exception exception)
        {
            // TODO: Implement exception handling logic
            return 10;
        }

        // The following methods need to be implemented based on the full SQL Server implementation
        // They are placeholders to allow compilation

        public override Task<TaskOrchestrationWorkItem?> LockNextTaskOrchestrationWorkItemAsync(
            TimeSpan receiveTimeout,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
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
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<TaskActivityWorkItem?> LockNextTaskActivityWorkItem(
            TimeSpan receiveTimeout,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<TaskActivityWorkItem?> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[] dedupeStatuses)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task SendTaskOrchestrationMessageAsync(TaskMessage message)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<OrchestrationState?> WaitForOrchestrationAsync(
            string instanceId,
            string? executionId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task ForceTerminateTaskOrchestrationAsync(string instanceId, string reason)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<string> GetOrchestrationHistoryAsync(string instanceId, string? executionId)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<OrchestrationState?> GetOrchestrationStateAsync(string instanceId, string? executionId)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<PurgeResult> PurgeInstanceStateAsync(string instanceId)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<PurgeResult> PurgeInstanceStateAsync(PurgeInstanceFilter purgeInstanceFilter)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task PurgeOrchestrationHistoryAsync(
            DateTime thresholdDateTimeUtc,
            OrchestrationStateTimeRangeFilterType timeRangeFilterType)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }

        public override Task<OrchestrationQueryResult> GetOrchestrationWithQueryAsync(
            OrchestrationQuery query,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException("This method needs to be implemented based on PostgreSQL stored procedures");
        }
    }
}
