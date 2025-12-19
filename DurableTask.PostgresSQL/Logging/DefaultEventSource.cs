namespace DurableTask.PostgresSQL.Logging
{
    using System.Diagnostics.Tracing;

    /// <summary>
    /// Event Source for DurableTask-PostgresSQL.
    /// </summary>
    /// <remarks>
    /// The names of the fields are intended to match those in DurableTask-Core and other providers.
    /// The provider GUID value is 5C9F8A23-F74E-6GE3-281E-79BD76C2F69E.
    /// </remarks>
    [EventSource(Name = "DurableTask-PostgresSQL")]
    class DefaultEventSource : EventSource
    {
        public static readonly DefaultEventSource Log = new DefaultEventSource();

        [Event(EventIds.ExecutedSqlScript, Level = EventLevel.Informational)]
        public void ExecutedSqlScript(
            string Name,
            long LatencyMs,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.ExecutedSqlScript,
                Name,
                LatencyMs,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.SprocCompleted, Level = EventLevel.Verbose)]
        public void SprocCompleted(
            string? InstanceId,
            string Name,
            long LatencyMs,
            int RetryCount,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.SprocCompleted,
                InstanceId ?? string.Empty,
                Name,
                LatencyMs,
                RetryCount,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.AcquiredAppLock, Level = EventLevel.Informational)]
        public void AcquiredAppLock(
            int StatusCode,
            long LatencyMs,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.AcquiredAppLock,
                StatusCode,
                LatencyMs,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.CheckpointStarting, Level = EventLevel.Informational)]
        public void CheckpointStarting(
            string InstanceId,
            string ExecutionId,
            string Name,
            string Status,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.CheckpointStarting,
                InstanceId,
                ExecutionId,
                Name,
                Status,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.CheckpointCompleted, Level = EventLevel.Informational)]
        public void CheckpointCompleted(
            string InstanceId,
            string ExecutionId,
            string Name,
            long LatencyMs,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.CheckpointCompleted,
                InstanceId,
                ExecutionId,
                Name,
                LatencyMs,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.ProcessingFailure, Level = EventLevel.Error)]
        public void ProcessingError(
            string Details,
            string InstanceId,
            string ExecutionId,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.ProcessingFailure,
                Details,
                InstanceId,
                ExecutionId ?? string.Empty,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.GenericWarning, Level = EventLevel.Warning)]
        internal void GenericWarning(
            string Details,
            string InstanceId,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.GenericWarning,
                Details,
                InstanceId,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.DuplicateExecutionDetected, Level = EventLevel.Warning)]
        internal void DuplicateExecutionDetected(
            string InstanceId,
            string ExecutionId,
            string Name,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.DuplicateExecutionDetected,
                InstanceId,
                ExecutionId ?? string.Empty,
                Name,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.TransientDatabaseFailure, Level = EventLevel.Warning)]
        internal void TransientDatabaseFailure(
            string InstanceId,
            string Details,
            int RetryCount,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.TransientDatabaseFailure,
                InstanceId,
                Details,
                RetryCount,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.ReplicaCountChangeRecommended, Level = EventLevel.Informational)]
        internal void ReplicaCountChangeRecommended(
            int CurrentCount,
            int RecommendedCount,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.ReplicaCountChangeRecommended,
                CurrentCount,
                RecommendedCount,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.PurgedInstances, Level = EventLevel.Informational)]
        internal void PurgedInstances(
            string UserId,
            int InstanceCount,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.PurgedInstances,
                UserId,
                InstanceCount,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.CommandCompleted, Level = EventLevel.Verbose)]
        internal void CommandCompleted(
            string CommandName,
            long LatencyMs,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.CommandCompleted,
                CommandName,
                LatencyMs,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.CreatedDatabase, Level = EventLevel.Informational)]
        internal void CreatedDatabase(
            string DatabaseName,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.CreatedDatabase,
                DatabaseName,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.DiscardingEvent, Level = EventLevel.Warning)]
        internal void DiscardingEvent(
            string InstanceId,
            string ExecutionId,
            string EventType,
            int TaskEventId,
            string Details,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.DiscardingEvent,
                InstanceId,
                ExecutionId ?? string.Empty,
                EventType,
                TaskEventId,
                Details,
                AppName,
                ExtensionVersion);
        }

        [Event(EventIds.GenericInfo, Level = EventLevel.Informational)]
        internal void GenericInfo(
            string Details,
            string InstanceId,
            string AppName,
            string ExtensionVersion)
        {
            this.WriteEvent(
                EventIds.GenericInfo,
                Details,
                InstanceId,
                AppName,
                ExtensionVersion);
        }
    }
}
