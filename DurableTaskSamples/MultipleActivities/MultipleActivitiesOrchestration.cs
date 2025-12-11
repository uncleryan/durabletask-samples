namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Basic orchestration scheduling multiple activities in sequence.
    /// 
    /// We notice the following things here:
    ///   - The orchestration runs multiple times, but each activity only executes once
    ///   - The orchestration completes execution as soon as it finds an activity to schedule
    /// </summary>
    public class MultipleActivitiesOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<MultipleActivitiesOrchestration> _logger;

        public MultipleActivitiesOrchestration(ILogger<MultipleActivitiesOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            try
            {
                _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);

                _logger.LogDebug("Scheduling FirstActivity");
                bool result = await context.ScheduleTask<bool>(typeof(FirstActivity), input);
                _logger.LogDebug("FirstActivity returned {Result}", result);

                _logger.LogDebug("Scheduling SecondActivity");
                result = await context.ScheduleTask<bool>(typeof(SecondActivity), input + 1);
                _logger.LogDebug("SecondActivity returned {Result}", result);

                _logger.LogInformation("Completed");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in orchestration");
                return false;
            }
        }
    }
}
