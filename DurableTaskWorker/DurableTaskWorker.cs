using DurableTask.Core;
using DurableTask.Core.Tracing;
using DurableTaskSamples.Common.Logging;
using DurableTaskSamples.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Practices.EnterpriseLibrary.SemanticLogging;
using System;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;

namespace DurableTaskSamples.DurableTaskWorker
{
    public class DurableTaskWorker(IConfiguration _configuration)
    {
        private TaskHubWorker taskHubWorker;

        private async Task InitializeTaskWorkerAsync()
        {
            this.taskHubWorker.AddTaskOrchestrations(
                typeof(SameActivityMultipleSchedulesOrchestration),
                typeof(MultipleActivitiesOrchestration),
                typeof(ContinueAsNewTestingOrchestration),
                typeof(ErrorHandlingWithContinueAsNewOrchestration),
                typeof(InlineForLoopTestingOrchestration),
                typeof(ErrorHandlingWithInlineRetriesOrchestration),
                typeof(FixedPollingWithInlineRetriesOrchestration),
                typeof(UnboundedPollingWithInlineRetriesOrchestration),
                typeof(UnboundedPollingWithContinueAsNewOrchestration));
            this.taskHubWorker.AddTaskActivities(
                new GreetingActivity(),
                new FirstActivity(),
                new SecondActivity(),
                new RetryableExceptionThrowingActivity(),
                new PollingActivity(),
                new AlwaysThrowingActivity());
            await this.taskHubWorker.StartAsync();
        }

        public async Task Start()
        {
            // Debug: Print the connection string to verify it's being loaded
            var connString = _configuration.GetConnectionString("durableDb");
            Console.WriteLine($"Connection String: {connString ?? "NULL - NOT FOUND"}");

            if (Utils.ShouldLogDtfCoreTraces(_configuration))
            {
                var eventListener = new ObservableEventListener();
                eventListener.LogToConsole(formatter: new DtfEventFormatter());
                eventListener.EnableEvents(DefaultEventSource.Log, EventLevel.Informational);
            }

            var orchestrationServiceAndClient = await Utils.GetSqlServerOrchestrationServiceClient(_configuration);
            Console.WriteLine(orchestrationServiceAndClient.ToString());
            this.taskHubWorker = new TaskHubWorker(orchestrationServiceAndClient);
            this.taskHubWorker.ErrorPropagationMode = ErrorPropagationMode.SerializeExceptions;

            await this.InitializeTaskWorkerAsync();
        }

        public async Task Stop()
        {
            await this.taskHubWorker.StopAsync(true);
        }

    }
}
