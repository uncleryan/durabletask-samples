namespace DurableTaskSamples.Otp
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;

    public class SendOtpActivity : TaskActivity<OtpRequest, bool>
    {
        private readonly ILogger<SendOtpActivity> _logger;

        public SendOtpActivity(ILogger<SendOtpActivity> logger)
        {
            _logger = logger;
        }

        protected override bool Execute(TaskContext context, OtpRequest input)
        {
            _logger.LogInformation("Sending OTP '{Code}' to user '{UserId}'", input.Code, input.UserId);
            // Simulate sending delay
            System.Threading.Thread.Sleep(100);
            _logger.LogInformation("OTP sent successfully");
            return true;
        }
    }
}
