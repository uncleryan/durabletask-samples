namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class PollingActivity : AsyncTaskActivity<int, bool>
    {
        private readonly ILogger<PollingActivity> _logger;
        private readonly int numPolls;

        public PollingActivity(ILogger<PollingActivity> logger, int numPolls = 8)
        {
            _logger = logger;
            this.numPolls = numPolls;
        }

        protected override async Task<bool> ExecuteAsync(TaskContext context, int input)
        {
            _logger.LogInformation("Starting");

            _logger.LogInformation("Performing async poll task attempt {Input}", input);
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            
            bool pollingResult = !(input < numPolls);
            _logger.LogInformation("Polling result: {PollingResult}", pollingResult);

            return pollingResult;
        }
    }
}
