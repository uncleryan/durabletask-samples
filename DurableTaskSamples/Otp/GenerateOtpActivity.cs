namespace DurableTaskSamples.Otp
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;

    public class GenerateOtpActivity : TaskActivity<string, string>
    {
        private readonly ILogger<GenerateOtpActivity> _logger;

        public GenerateOtpActivity(ILogger<GenerateOtpActivity> logger)
        {
            _logger = logger;
        }

        protected override string Execute(TaskContext context, string input)
        {
            var random = new Random();
            var code = random.Next(100000, 999999).ToString();
            _logger.LogInformation("Generated OTP: {Code} for user: {Input}", code, input);
            return code;
        }
    }
}
