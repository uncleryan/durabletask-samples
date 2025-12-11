namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class UnboundedPollingWithInlineRetriesOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<UnboundedPollingWithInlineRetriesOrchestration> _logger;
        private const int PollingIntervalInSeconds = 10;
        private const int MaxPollingPerOrchestrationInstance = 5;

        public UnboundedPollingWithInlineRetriesOrchestration(ILogger<UnboundedPollingWithInlineRetriesOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);

            bool result = false;
            for (int i = 0; i < MaxPollingPerOrchestrationInstance; i++)
            {
                _logger.LogInformation("Polling attempt {Attempt}", input + i);
                result = await context.ScheduleTask<bool>(typeof(PollingActivity), input + i);

                if (result)
                {
                    _logger.LogInformation("Polling success");
                    break;
                }
                else
                {
                    _logger.LogDebug("Scheduling next poll after {PollingInterval} seconds.", PollingIntervalInSeconds);
                    await context.CreateTimer<int>(context.CurrentUtcDateTime.AddSeconds(PollingIntervalInSeconds), input + 1);
                }
            }

            if (result)
            {
                _logger.LogInformation("Completed");
                return true;
            }
            else
            {
                _logger.LogInformation("{RetryCount} retries were not enough, continuing as new", input + MaxPollingPerOrchestrationInstance);
                context.ContinueAsNew(input + MaxPollingPerOrchestrationInstance);
            }

            return false;
        }
    }
}
