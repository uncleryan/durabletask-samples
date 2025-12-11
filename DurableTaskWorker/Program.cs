namespace DurableTaskSamples.DurableTaskWorker
{
    using DurableTask.Core.Settings;
    using DurableTaskSamples.Common.Utils;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using OpenTelemetry;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    internal class Program
    {
        static ManualResetEvent _quitEvent = new ManualResetEvent(false);

        static async Task Main(string[] args)
        {
            CorrelationSettings.Current.EnableDistributedTracing = true;

            // Build host with OpenTelemetry configured for Aspire
            var builder = Host.CreateApplicationBuilder(args);

            // Configure OpenTelemetry for traces and metrics
            ConfigureOpenTelemetry(builder);

            var host = builder.Build();

            // Start the host to begin OpenTelemetry export
            await host.StartAsync();

            var configuration = builder.Configuration;

            Console.CancelKeyPress += (sender, eArgs) =>
            {
                _quitEvent.Set();
                eArgs.Cancel = true;
            };

            var taskHubWorker = new DurableTaskWorker(configuration, host.Services.GetRequiredService<ILoggerFactory>());
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
                await host.StopAsync();
            }
        }

        private static void ConfigureOpenTelemetry(HostApplicationBuilder builder)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter("Microsoft.Data.SqlClient");
                })
                .WithTracing(tracing =>
                {
                    tracing.AddSource(builder.Environment.ApplicationName)
                        .AddSource("DurableTask.Core")
                        .AddHttpClientInstrumentation()
                        .AddSqlClientInstrumentation(options =>
                        {
                            options.SetDbStatementForText = true;
                            options.RecordException = true;
                        });
                });

            var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
            if (useOtlpExporter)
            {
                builder.Services.AddOpenTelemetry().UseOtlpExporter();
            }
        }
    }
}

