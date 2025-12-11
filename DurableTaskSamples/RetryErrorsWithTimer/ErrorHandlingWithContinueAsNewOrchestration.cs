namespace DurableTaskSamples
{
    using DurableTask.Core;
    using DurableTask.Core.Exceptions;
    using DurableTaskSamples.Common.Utils;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class ErrorHandlingWithContinueAsNewOrchestration : TaskOrchestration<bool, int>
    {
        private readonly ILogger<ErrorHandlingWithContinueAsNewOrchestration> _logger;

        public ErrorHandlingWithContinueAsNewOrchestration(ILogger<ErrorHandlingWithContinueAsNewOrchestration> logger)
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
            
            try
            {
                bool result = await context.ScheduleWithRetry<bool>(typeof(RetryableExceptionThrowingActivity), retryOptions, input);
                _logger.LogInformation("RetryableExceptionThrowingActivity returned {Result}", result);
                _logger.LogInformation("Completed");
                return result;
            }
            catch (TaskFailedException ex)
            {
                int retryAfterInSeconds = Utils.GetRetryAfterSecondsFromException(ex);
                _logger.LogInformation("Error in activity, scheduling retry after {RetryAfterSeconds}", retryAfterInSeconds);
                int newInput = await context.CreateTimer<int>(context.CurrentUtcDateTime.AddSeconds(retryAfterInSeconds), input + 1);
                _logger.LogInformation("Timer elapsed");
                context.ContinueAsNew(newInput);
            }
            
            return false;
        }
    }
}
