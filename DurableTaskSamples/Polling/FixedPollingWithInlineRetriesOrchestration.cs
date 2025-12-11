namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class FixedPollingWithInlineRetriesOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<FixedPollingWithInlineRetriesOrchestration> _logger;
        private const int PollingIntervalInSeconds = 10;

        public FixedPollingWithInlineRetriesOrchestration(ILogger<FixedPollingWithInlineRetriesOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);

            for (int i = 0; i < input; i++)
            {
                _logger.LogDebug("Polling attempt {Attempt}", i);
                bool result = await context.ScheduleTask<bool>(typeof(PollingActivity), i);

                if (result)
                {
                    _logger.LogInformation("Polling success");
                    break;
                }
                else
                {
                    _logger.LogDebug("Scheduling next poll after {PollingInterval} seconds.", PollingIntervalInSeconds);
                    await context.CreateTimer<int>(context.CurrentUtcDateTime.AddSeconds(PollingIntervalInSeconds), input + 1);
                    _logger.LogDebug("Poll timer for attempt {Attempt} elapsed.", i);
                }
            }

            _logger.LogInformation("Completed");
            return true;
        }
    }
}
