namespace DurableTaskSamples
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System.Threading.Tasks;

    public class GreetingActivity : AsyncTaskActivity<int, bool>
    {
        private readonly ILogger<GreetingActivity> _logger;

        public GreetingActivity(ILogger<GreetingActivity> logger)
        {
            _logger = logger;
        }

        protected override async Task<bool> ExecuteAsync(TaskContext context, int input)
        {
            _logger.LogInformation("Starting");
            await Task.Delay(5).ConfigureAwait(false);
            _logger.LogInformation("Executing {Input}", input);
            _logger.LogInformation("Completed");

            await Task.Delay(2000);
            
            if (input < 2)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
