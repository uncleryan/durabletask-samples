namespace DurableTaskSamples.Otp
{
    using DurableTask.Core;
    using DurableTaskSamples.Common.Logging;
    using System;
    using System.Threading.Tasks;

    public class OtpOrchestration : TaskOrchestration<bool, string>
    {
        private const string Source = "OtpOrchestration";
        private readonly TaskCompletionSource<string> _otpTcs = new TaskCompletionSource<string>();

        public override void OnEvent(OrchestrationContext context, string name, string input)
        {
            if (string.Equals(name, "OtpSubmit", StringComparison.OrdinalIgnoreCase))
            {
                _otpTcs.TrySetResult(input);
            }
        }

        public override async Task<bool> RunTask(OrchestrationContext context, string userId)
        {
            Logger.Log(Source, $"Starting OTP flow for user {userId}");

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
                    Logger.Log(Source, "OTP verified successfully");
                    return true;
                }
                else
                {
                    Logger.Log(Source, "OTP verification failed");
                    return false;
                }
            }
            else
            {
                Logger.Log(Source, "OTP timed out");
                return false;
            }
        }
    }
}
