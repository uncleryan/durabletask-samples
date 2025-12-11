namespace DurableTaskClient
{
    using DurableTask.Core.Settings;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using OpenTelemetry;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;
    using System.Threading.Tasks;

    internal class Program
    {
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

            while (true)
            {
                await CommandLineClient.Start(builder.Configuration);
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
