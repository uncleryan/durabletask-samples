namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class InlineForLoopTestingOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<InlineForLoopTestingOrchestration> _logger;

        public InlineForLoopTestingOrchestration(ILogger<InlineForLoopTestingOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            try
            {
                _logger.LogInformation("Initiating, IsReplaying: {IsReplaying}", context.IsReplaying);

                for (int i = 0; i < input; i++)
                {
                    _logger.LogDebug("Executing for {Index}", i);
                    await context.ScheduleTask<bool>(typeof(GreetingActivity), i);
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
