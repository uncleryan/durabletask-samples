namespace DurableTaskSamples.Otp
{
    using DurableTask.Core;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;

    public class OtpOrchestration : TaskOrchestration<bool, string>
    {
        private readonly ILogger<OtpOrchestration> _logger;
        private readonly TaskCompletionSource<string> _otpTcs = new TaskCompletionSource<string>();

        public OtpOrchestration(ILogger<OtpOrchestration> logger)
        {
            _logger = logger;
        }

        public override void OnEvent(OrchestrationContext context, string name, string input)
        {
            if (string.Equals(name, "OtpSubmit", StringComparison.OrdinalIgnoreCase))
            {
                _otpTcs.TrySetResult(input);
            }
        }

        public override async Task<bool> RunTask(OrchestrationContext context, string userId)
        {
            _logger.LogInformation("Starting OTP flow for user {UserId}", userId);

            // Generate OTP
            var code = await context.ScheduleTask<string>(typeof(GenerateOtpActivity), userId);
            
            // Send OTP
            await context.ScheduleTask<bool>(typeof(SendOtpActivity), new OtpRequest { UserId = userId, Code = code });

            // Wait for OTP submission or timeout
            var timer = context.CreateTimer(context.CurrentUtcDateTime.AddMinutes(5), "Timeout");
            var eventTask = _otpTcs.Task;

            var winner = await Task.WhenAny(timer, eventTask);

            if (winner == eventTask)
            {
                var inputCode = eventTask.Result;
                if (string.Equals(inputCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("OTP verified successfully");
                    return true;
                }
                else
                {
                    _logger.LogInformation("OTP verification failed");
                    return false;
                }
            }
            else
            {
                _logger.LogInformation("OTP timed out");
                return false;
            }
        }
    }
}
