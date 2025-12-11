namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Basic orchestration while schedules the same activity multiple times
    /// We notice the following things here:
    ///   - When the same activity is schedule multiple times separately, each is a separate instance
    ///   - Same activity can be scheduled again with same input - the orchestration is smart enough to
    ///     identify that this is a separate invocation and not the first one.
    ///   - Each instance of the activity is only executed once, even the orchestration runs multiple times
    /// </summary>
    public class SameActivityMultipleSchedulesOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<SameActivityMultipleSchedulesOrchestration> _logger;

        public SameActivityMultipleSchedulesOrchestration(ILogger<SameActivityMultipleSchedulesOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            try
            {
                _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);
                await context.ScheduleTask<bool>(typeof(GreetingActivity), input);
                await context.ScheduleTask<bool>(typeof(GreetingActivity), input);
                await context.ScheduleTask<bool>(typeof(GreetingActivity), 42);
                _logger.LogInformation("Completed");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in orchestration");
                return false;
            }
        }
    }
}
