namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;

    public class SecondActivity : TaskActivity<int, bool>
    {
        private readonly ILogger<SecondActivity> _logger;

        public SecondActivity(ILogger<SecondActivity> logger)
        {
            _logger = logger;
        }

        protected override bool Execute(TaskContext context, int input)
        {
            _logger.LogInformation("Starting");
            _logger.LogInformation("Executing {Input}", input);
            _logger.LogInformation("Completed");
            return true;
        }
    }
}
