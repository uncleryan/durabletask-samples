namespace DurableTaskSamples
{
    using DurableTask.Core;
    using DurableTaskSamples.Common.Exceptions;
    using Microsoft.Extensions.Logging;

    public class AlwaysThrowingActivity : TaskActivity<int, bool>
    {
        private readonly ILogger<AlwaysThrowingActivity> _logger;
        private readonly int retryAfterSeconds;
        
        public AlwaysThrowingActivity(ILogger<AlwaysThrowingActivity> logger, int retryAfterSeconds = 5)
        {
            _logger = logger;
            this.retryAfterSeconds = retryAfterSeconds;
        }

        protected override bool Execute(TaskContext context, int input)
        {
            _logger.LogInformation("Starting");
            _logger.LogInformation("Executing {Input}", input);

            _logger.LogInformation("Throwing");
            throw new RetryableWithDelayException(this.retryAfterSeconds, $"My job is to throw always. ");
        }
    }
}
