namespace DurableTaskSamples.DurableTaskWorker
{
    using DurableTaskSamples.Common.Logging;
    using DurableTaskSamples.Common.Utils;
    using Microsoft.Extensions.Configuration;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal class Program
    {
        static ManualResetEvent _quitEvent = new ManualResetEvent(false);

        static async Task Main(string[] args)
        {
            // Build configuration from multiple sources including Aspire-injected values
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()  // Aspire injects connection strings via environment variables
                .Build();

            if (args.Length == 0)
            {
                if (Utils.ShouldDisableVerboseLogsInOrchestration(configuration))
                {
                    Logger.SetVerbosity(false);
                }
            }

            if (args.Length == 1)
            {
                if (args[0] == "disableVerboseLogs")
                {
                    Logger.SetVerbosity(false);
                }
            }

            Console.CancelKeyPress += (sender, eArgs) =>
            {
                _quitEvent.Set();
                eArgs.Cancel = true;
            };

            var taskHubWorker = new DurableTaskWorker(configuration);
            try
            {
                Console.WriteLine("Initializing worker");
                await taskHubWorker.Start();

                Console.WriteLine("Started TaskhubWorker, press Ctrl-C to stop");
                _quitEvent.WaitOne();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                await taskHubWorker.Stop();
            }

        }
    }
}

