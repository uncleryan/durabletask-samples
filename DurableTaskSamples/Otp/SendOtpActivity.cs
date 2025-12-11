namespace DurableTaskSamples.Otp
{
    using DurableTask.Core;
    using DurableTaskSamples.Common.Logging;
    using System;

    public class SendOtpActivity : TaskActivity<OtpRequest, bool>
    {
        private const string Source = "SendOtpActivity";

        protected override bool Execute(TaskContext context, OtpRequest input)
        {
            Logger.Log(Source, $"Sending OTP '{input.Code}' to user '{input.UserId}'");
            // Simulate sending delay
            System.Threading.Thread.Sleep(100);
            Logger.Log(Source, "OTP sent successfully");
            return true;
        }
    }
}
