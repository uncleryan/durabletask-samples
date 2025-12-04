using System;

namespace DurableTaskSamples.Common.Exceptions
{
    public class RetryableWithDelayException : Exception
    {
        public static readonly string IdentifierString = "Expected to retry after ";

        public RetryableWithDelayException(int retryAfter, string message)
            : base(message + IdentifierString + retryAfter.ToString())
        {
            RetryAfterInSeconds = retryAfter;
        }

        public int RetryAfterInSeconds { get; set; }
    }
}
