using DurableTask.Core;
using DurableTask.Core.Tracing;
using DurableTaskSamples.Common.Logging;
using DurableTaskSamples.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Practices.EnterpriseLibrary.SemanticLogging;
using System;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using DurableTaskSamples.Otp;

namespace DurableTaskSamples.DurableTaskWorker
{
    public class DurableTaskWorker(IConfiguration _configuration, ILoggerFactory _loggerFactory)
    {
        private TaskHubWorker taskHubWorker;

        private async Task InitializeTaskWorkerAsync()
        {
            this.taskHubWorker.AddTaskOrchestrations(
                new DefaultObjectCreator<TaskOrchestration, SameActivityMultipleSchedulesOrchestration>(() => new SameActivityMultipleSchedulesOrchestration(_loggerFactory.CreateLogger<SameActivityMultipleSchedulesOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, MultipleActivitiesOrchestration>(() => new MultipleActivitiesOrchestration(_loggerFactory.CreateLogger<MultipleActivitiesOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, ContinueAsNewTestingOrchestration>(() => new ContinueAsNewTestingOrchestration(_loggerFactory.CreateLogger<ContinueAsNewTestingOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, ErrorHandlingWithContinueAsNewOrchestration>(() => new ErrorHandlingWithContinueAsNewOrchestration(_loggerFactory.CreateLogger<ErrorHandlingWithContinueAsNewOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, InlineForLoopTestingOrchestration>(() => new InlineForLoopTestingOrchestration(_loggerFactory.CreateLogger<InlineForLoopTestingOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, ErrorHandlingWithInlineRetriesOrchestration>(() => new ErrorHandlingWithInlineRetriesOrchestration(_loggerFactory.CreateLogger<ErrorHandlingWithInlineRetriesOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, FixedPollingWithInlineRetriesOrchestration>(() => new FixedPollingWithInlineRetriesOrchestration(_loggerFactory.CreateLogger<FixedPollingWithInlineRetriesOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, UnboundedPollingWithInlineRetriesOrchestration>(() => new UnboundedPollingWithInlineRetriesOrchestration(_loggerFactory.CreateLogger<UnboundedPollingWithInlineRetriesOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, UnboundedPollingWithContinueAsNewOrchestration>(() => new UnboundedPollingWithContinueAsNewOrchestration(_loggerFactory.CreateLogger<UnboundedPollingWithContinueAsNewOrchestration>())),
                new DefaultObjectCreator<TaskOrchestration, OtpOrchestration>(() => new OtpOrchestration(_loggerFactory.CreateLogger<OtpOrchestration>())));

            this.taskHubWorker.AddTaskActivities(
                new GreetingActivity(_loggerFactory.CreateLogger<GreetingActivity>()),
                new FirstActivity(_loggerFactory.CreateLogger<FirstActivity>()),
                new SecondActivity(_loggerFactory.CreateLogger<SecondActivity>()),
                new RetryableExceptionThrowingActivity(_loggerFactory.CreateLogger<RetryableExceptionThrowingActivity>()),
                new PollingActivity(_loggerFactory.CreateLogger<PollingActivity>()),
                new AlwaysThrowingActivity(_loggerFactory.CreateLogger<AlwaysThrowingActivity>()),
                new GenerateOtpActivity(_loggerFactory.CreateLogger<GenerateOtpActivity>()),
                new SendOtpActivity(_loggerFactory.CreateLogger<SendOtpActivity>()));

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

    public class DefaultObjectCreator<TBase, T> : ObjectCreator<TBase> where T : TBase
    {
        private readonly Func<T> _factory;

        public DefaultObjectCreator(Func<T> factory)
        {
            _factory = factory;
            Name = typeof(T).Name;
            Version = string.Empty;
        }

        public override TBase Create()
        {
            return _factory();
        }
    }
}
