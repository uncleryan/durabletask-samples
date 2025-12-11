namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// This orchestration helps understand the ContinueAsNew behavior
    /// </summary>
    public class ContinueAsNewTestingOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<ContinueAsNewTestingOrchestration> _logger;

        public ContinueAsNewTestingOrchestration(ILogger<ContinueAsNewTestingOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            try
            {
                _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);
                await context.ScheduleTask<bool>(typeof(GreetingActivity), input);
                
                if (input < 3)
                {
                    context.ContinueAsNew(input + 1);
                }

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
