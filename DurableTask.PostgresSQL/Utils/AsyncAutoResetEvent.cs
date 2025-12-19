namespace DurableTask.PostgresSQL.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    // Inspired by https://blogs.msdn.microsoft.com/pfxteam/2012/02/11/building-async-coordination-primitives-part-2-asyncautoresetevent/
    class AsyncAutoResetEvent
    {
        readonly LinkedList<TaskCompletionSource<bool>> waiters =
            new LinkedList<TaskCompletionSource<bool>>();

        bool isSignaled;

        public AsyncAutoResetEvent(bool signaled)
        {
            this.isSignaled = signaled;
        }

        public Task<bool> WaitAsync(TimeSpan timeout)
        {
            return this.WaitAsync(timeout, CancellationToken.None);
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> tcs;

            lock (this.waiters)
            {
                if (this.isSignaled)
                {
                    this.isSignaled = false;
                    return true;
                }
                else if (timeout == TimeSpan.Zero)
                {
                    return this.isSignaled;
                }
                else
                {
                    tcs = new TaskCompletionSource<bool>();
                    this.waiters.AddLast(tcs);
                }
            }

            Task winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cancellationToken));
            if (winner == tcs.Task)
            {
                return true;
            }
            else
            {
                lock (this.waiters)
                {
                    this.waiters.Remove(tcs);
                }

                return false;
            }
        }

        public void Set()
        {
            TaskCompletionSource<bool>? toRelease = null;

            lock (this.waiters)
            {
                if (this.waiters.Count > 0)
                {
                    toRelease = this.waiters.First!.Value;
                    this.waiters.RemoveFirst();
                }
                else if (!this.isSignaled)
                {
                    this.isSignaled = true;
                }
            }

            toRelease?.SetResult(true);
        }
    }
}
