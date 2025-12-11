namespace DurableTaskSamples.Otp
{
    using DurableTask.Core;
    using DurableTaskSamples.Common.Logging;
    using System;

    public class GenerateOtpActivity : TaskActivity<string, string>
    {
        private const string Source = "GenerateOtpActivity";

        protected override string Execute(TaskContext context, string input)
        {
            var random = new Random();
            var code = random.Next(100000, 999999).ToString();
            Logger.Log(Source, $"Generated OTP: {code} for user: {input}");
            return code;
        }
    }
}
