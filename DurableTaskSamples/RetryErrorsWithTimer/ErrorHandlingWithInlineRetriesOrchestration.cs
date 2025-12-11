namespace DurableTaskSamples
{
    using DurableTask.Core;
    using DurableTask.Core.Exceptions;
    using DurableTaskSamples.Common.Utils;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class ErrorHandlingWithInlineRetriesOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<ErrorHandlingWithInlineRetriesOrchestration> _logger;

        public ErrorHandlingWithInlineRetriesOrchestration(ILogger<ErrorHandlingWithInlineRetriesOrchestration> logger)
        {
            _logger = logger;
        }

        public override async Task<bool> RunTask(OrchestrationContext context, int input)
        {
            _logger.LogInformation("Starting");
            var retryOptions = new RetryOptions(TimeSpan.FromSeconds(1), 2)
            {
                Handle = (ex) =>
                {
                    _logger.LogInformation("Exception type: {ExceptionType}", ex.GetType().Name);
                    return !Utils.IsCustomRetryException((TaskFailedException)ex);
                }
            };

            for (int i = 0; i < input; i++)
            {
                _logger.LogInformation("Attempt {Attempt}", i);
                try
                {
                    bool result = await context.ScheduleWithRetry<bool>(typeof(AlwaysThrowingActivity), retryOptions, i);
                    _logger.LogInformation("AlwaysThrowingActivity returned {Result}", result);
                    return result;
                }
                catch (TaskFailedException ex)
                {
                    int retryAfterInSeconds = Utils.GetRetryAfterSecondsFromException(ex);
                    _logger.LogInformation("Error in activity, scheduling retry after {RetryAfterSeconds}", retryAfterInSeconds);
                    int newInput = await context.CreateTimer<int>(context.CurrentUtcDateTime.AddSeconds(retryAfterInSeconds), input + 1);
                    _logger.LogInformation("Timer elapsed");
                }
            }

            _logger.LogInformation("Retry attempts exhausted");
            return false;
        }
    }
}
