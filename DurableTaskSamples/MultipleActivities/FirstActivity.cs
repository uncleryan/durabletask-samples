namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;

    public class FirstActivity : TaskActivity<int, bool>
    {
        private readonly ILogger logger;

        public FirstActivity(ILogger logger)
        {
            this.logger = logger;
        }

        protected override bool Execute(TaskContext context, int input)
        {
            this.logger.LogInformation("Starting");
            this.logger.LogInformation($"Executing {input}");
            this.logger.LogInformation("Completed");
            return true;
        }
    }
}
