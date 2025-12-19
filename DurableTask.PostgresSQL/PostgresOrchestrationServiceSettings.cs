namespace DurableTask.PostgresSQL
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Newtonsoft.Json;
    using Npgsql;
    using System;

    /// <summary>
    /// Configuration settings for the <see cref="PostgresOrchestrationService"/>.
    /// </summary>
    public class PostgresOrchestrationServiceSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PostgresOrchestrationServiceSettings"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string for connecting to the database.</param>
        /// <param name="taskHubName">Optional. The name of the task hub. If not specified, a default name will be used.</param>
        /// <param name="schemaName">Optional. The name of the schema. If not specified, the default 'dt' value will be used.</param>
        public PostgresOrchestrationServiceSettings(string connectionString, string? taskHubName = null, string? schemaName = null)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            this.TaskHubName = taskHubName ?? "default";
            this.SchemaName = schemaName ?? "dt";

            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                // We use the task hub name as the application name so that
                // stored procedures have easy access to this information.
                ApplicationName = this.TaskHubName,
            };

            if (string.IsNullOrEmpty(builder.Database))
            {
                throw new ArgumentException("Database must be specified in the connection string.", nameof(connectionString));
            }

            this.DatabaseName = builder.Database;
            this.TaskHubConnectionString = builder.ToString();
        }

        /// <summary>
        /// Gets or sets the number of events that can be dequeued at a time.
        /// </summary>
        [JsonProperty("workItemBatchSize")]
        public int WorkItemBatchSize { get; set; } = 10;

        /// <summary>
        /// Gets or sets the amount of time a work item is locked after being dequeued.
        /// </summary>
        [JsonProperty("workItemLockTimeout")]
        public TimeSpan WorkItemLockTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Gets or sets the name of the task hub.
        /// </summary>
        [JsonProperty("taskHubName")]
        public string TaskHubName { get; }

        /// <summary>
        /// Gets the name of the schema.
        /// </summary>
        [JsonProperty("schemaName")]
        public string SchemaName { get; }

        /// <summary>
        /// Gets or sets the name of the app. Used for logging purposes.
        /// </summary>
        [JsonProperty("appName")]
        public string AppName { get; set; } = Environment.MachineName;

        /// <summary>
        /// Gets or sets the maximum number of work items that can be processed concurrently by a single worker.
        /// The default value is the value of <see cref="Environment.ProcessorCount"/>.
        /// </summary>
        [JsonProperty("maxConcurrentActivities")]
        public int MaxConcurrentActivities { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the maximum number of orchestrations that can be loaded in memory at a time by a single worker.
        /// The default value is the value of <see cref="Environment.ProcessorCount"/>.
        /// </summary>
        /// <remarks>
        /// Orchestrations that are idle and waiting for inputs are unloaded from memory and do not count against this limit.
        /// </remarks>
        [JsonProperty("maxActiveOrchestrations")]
        public int MaxActiveOrchestrations { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the minimum interval to poll for orchestrations.
        /// Polling interval increases when no orchestrations or activities are found.
        /// The default value is 50 milliseconds.
        /// </summary>
        [JsonProperty("minOrchestrationPollingInterval")]
        public TimeSpan MinOrchestrationPollingInterval { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Gets or sets the maximum interval to poll for orchestrations.
        /// Polling interval increases when no orchestrations or activities are found.
        /// The default value is 3 seconds.
        /// </summary>
        [JsonProperty("maxOrchestrationPollingInterval")]
        public TimeSpan MaxOrchestrationPollingInterval { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the delta backoff interval to poll for orchestrations.
        /// Polling interval increases by this delta when no orchestrations are found.
        /// The default value is 50 milliseconds.
        /// </summary>
        [JsonProperty("deltaBackoffOrchestrationPollingInterval")]
        public TimeSpan DeltaBackoffOrchestrationPollingInterval { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Gets or sets the minimum interval to poll for activities.
        /// Polling interval increases when no activities are found.
        /// The default value is 50 milliseconds.
        /// </summary>
        [JsonProperty("minActivityPollingInterval")]
        public TimeSpan MinActivityPollingInterval { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Gets or sets the maximum interval to poll for activities.
        /// Polling interval increases when no activities are found.
        /// The default value is 3 seconds.
        /// </summary>
        [JsonProperty("maxActivityPollingInterval")]
        public TimeSpan MaxActivityPollingInterval { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the delta backoff interval to poll for activities.
        /// Polling interval increases by this delta when no activities are found.
        /// The default value is 50 milliseconds.
        /// </summary>
        [JsonProperty("deltaBackoffActivityPollingInterval")]
        public TimeSpan DeltaBackoffActivityPollingInterval { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Gets or sets a flag indicating whether the database should be automatically created if it does not exist.
        /// </summary>
        /// <remarks>
        /// If <see langword="true"/>, the user requires the permission <c>CREATE DATABASE</c>.
        /// </remarks>
        [JsonProperty("createDatabaseIfNotExists")]
        public bool CreateDatabaseIfNotExists { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating whether to log replays of orchestration actions.
        /// </summary>
        [JsonProperty("logReplayEvents")]
        public bool LogReplayEvents { get; set; }

        /// <summary>
        /// Gets or sets a factory for creating <see cref="ILoggerFactory"/> instances.
        /// </summary>
        [JsonIgnore]
        public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

        internal string DatabaseName { get; }

        internal string TaskHubConnectionString { get; }

        internal NpgsqlConnection CreateConnection() => new NpgsqlConnection(this.TaskHubConnectionString);
    }
}
