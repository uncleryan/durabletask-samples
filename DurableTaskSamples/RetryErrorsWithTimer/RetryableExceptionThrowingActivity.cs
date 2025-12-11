namespace DurableTaskSamples
{
    using DurableTask.Core;
    using DurableTaskSamples.Common.Exceptions;
    using Microsoft.Extensions.Logging;

    public class RetryableExceptionThrowingActivity : TaskActivity<int, bool>
    {
        private readonly ILogger<RetryableExceptionThrowingActivity> _logger;
        private readonly int numThrows;
        private readonly int retryAfterSeconds;

        public RetryableExceptionThrowingActivity(ILogger<RetryableExceptionThrowingActivity> logger, int numThrows = 5, int retryAfterSeconds = 10)
        {
            _logger = logger;
            this.numThrows = numThrows;
            this.retryAfterSeconds = retryAfterSeconds;
        }

        protected override bool Execute(TaskContext context, int input)
        {
            _logger.LogInformation("Starting");
            _logger.LogInformation("Executing {Input}", input);

            if (input < this.numThrows)
            {
                _logger.LogInformation("Throwing");
                throw new RetryableWithDelayException(this.retryAfterSeconds, $"My job is to throw {this.numThrows} times.");
            }
            else
            {
                _logger.LogInformation("Completed");
                return true;
            }
        }
    }
}
