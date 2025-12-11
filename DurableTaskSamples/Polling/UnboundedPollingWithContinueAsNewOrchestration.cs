namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class UnboundedPollingWithContinueAsNewOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<UnboundedPollingWithContinueAsNewOrchestration> _logger;
        private const int PollingIntervalInSeconds = 10;

        public UnboundedPollingWithContinueAsNewOrchestration(ILogger<UnboundedPollingWithContinueAsNewOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);

            _logger.LogDebug("Polling attempt {Attempt}", input);
            bool result = await context.ScheduleTask<bool>(typeof(PollingActivity), input);

            if (result)
            {
                _logger.LogInformation("Polling success");
                _logger.LogInformation("Completed");
                return true;
            }
            else
            {
                _logger.LogDebug("Polling did not return success, scheduling next poll after {PollingInterval} seconds.", PollingIntervalInSeconds);
                int newInput = await context.CreateTimer<int>(context.CurrentUtcDateTime.AddSeconds(PollingIntervalInSeconds), input + 1);
                _logger.LogInformation("Polling timer elapsed, continuing as new");
                context.ContinueAsNew(newInput);
            }

            return false;
        }
    }
}
