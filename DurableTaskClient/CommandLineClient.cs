using DurableTask.Core;
using DurableTaskSamples;
using DurableTaskSamples.Common.Utils;
using DurableTaskSamples.Otp;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DurableTaskClient
{
    public class CommandLineClient
    {
        private static readonly Dictionary<int, Type> commandLineOptions = new Dictionary<int, Type>()
        {
            { 1, typeof(SameActivityMultipleSchedulesOrchestration)},
            { 2, typeof(MultipleActivitiesOrchestration)},
            { 3, typeof(ContinueAsNewTestingOrchestration)},
            { 4, typeof(ErrorHandlingWithContinueAsNewOrchestration)},
            { 5, typeof(InlineForLoopTestingOrchestration)},
            { 6, typeof(ErrorHandlingWithInlineRetriesOrchestration)},
            { 7, typeof(FixedPollingWithInlineRetriesOrchestration)},
            { 8, typeof(UnboundedPollingWithInlineRetriesOrchestration)},
            { 9, typeof(UnboundedPollingWithContinueAsNewOrchestration)},
            { 10, typeof(OtpOrchestration)},
        };

        private static readonly Dictionary<Type, object> orchestrationInputs = new Dictionary<Type, object>()
        {
            { typeof(SameActivityMultipleSchedulesOrchestration), 1},
            { typeof(MultipleActivitiesOrchestration), 1},
            { typeof(ContinueAsNewTestingOrchestration), 0},
            { typeof(ErrorHandlingWithContinueAsNewOrchestration), 0},
            { typeof(InlineForLoopTestingOrchestration), 5},
            { typeof(ErrorHandlingWithInlineRetriesOrchestration), 5},
            { typeof(FixedPollingWithInlineRetriesOrchestration), 10},
            { typeof(UnboundedPollingWithInlineRetriesOrchestration), 0},
            { typeof(UnboundedPollingWithContinueAsNewOrchestration), 0},
            { typeof(OtpOrchestration), "User1"},
        };

        private static void PrintCommandLine()
        {
            Utils.WriteToConsoleWithColor("Select an option:", ConsoleColor.Yellow);

            foreach (KeyValuePair<int, Type> kvp in commandLineOptions)
            {
                int key = kvp.Key;
                string value = kvp.Value.Name;

                Console.WriteLine($"{key}. {value}");
            }

            Utils.WriteToConsoleWithColor("Enter you input: ", ConsoleColor.Yellow);
        }

        public static async Task Start(IConfiguration configuration)
        {
            // Debug: Print the connection string to verify it's being loaded
            var connString = configuration.GetConnectionString("durableDb");
            Console.WriteLine($"Connection String: {connString ?? "NULL - NOT FOUND"}");

            PrintCommandLine();
            if (!int.TryParse(Console.ReadLine(), out int input))
            {
                Console.WriteLine("Invalid input");
                Environment.Exit(0);
            }

            if (!commandLineOptions.TryGetValue(input, out Type orchestrationSample))
            {
                Console.WriteLine("Invalid option");
                Environment.Exit(0);
            }

            Console.WriteLine($"Executing {orchestrationSample.Name}");
            string instanceId = Guid.NewGuid().ToString();

            var orchestrationServiceAndClient = await Utils.GetSqlServerOrchestrationServiceClient(configuration);
            Console.WriteLine(orchestrationServiceAndClient.ToString());

            var taskHubClient = new TaskHubClient(orchestrationServiceAndClient);

            try
            {
                var orchestrationInput = orchestrationInputs[orchestrationSample];
                var instance = await taskHubClient.CreateOrchestrationInstanceAsync(orchestrationSample, instanceId, orchestrationInput);
                Console.WriteLine("Workflow Instance Started: " + instance);

                if (orchestrationSample == typeof(OtpOrchestration))
                {
                    Console.WriteLine("OTP Orchestration started. Please check the worker logs for the generated OTP.");
                    Console.Write("Enter the OTP: ");
                    string otp = Console.ReadLine();
                    await taskHubClient.RaiseEventAsync(instance, "OtpSubmit", otp);
                }

                if (Utils.ShouldLaunchInstanceManager(configuration))
                {
                    string managerPath = GetDurableTaskManagerPath();
                    if (File.Exists(managerPath))
                    {
                        using (var p = new Process())
                        {
                            p.StartInfo.FileName = managerPath;
                            p.StartInfo.Arguments = instanceId;
                            p.StartInfo.UseShellExecute = true;
                            p.Start();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not find DurableTaskManager.exe at {managerPath}");
                    }
                }

                int timeout = 5;
                Console.WriteLine($"Waiting up to {timeout} minutes for completion.");

                OrchestrationState taskResult = await taskHubClient.WaitForOrchestrationAsync(instance, TimeSpan.FromMinutes(timeout), CancellationToken.None);
                Utils.WriteToConsoleWithColor($"Task done. Orchestration status: {taskResult?.OrchestrationStatus}", ConsoleColor.Green);
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static string GetDurableTaskManagerPath()
        {
            // Try relative to project root (if working dir is project root)
            string relativePath = Path.Combine("..", "DurableTaskManager", "bin", "Debug", "net10.0", "DurableTaskManager.exe");
            string absolutePath = Path.GetFullPath(relativePath);
            if (File.Exists(absolutePath))
            {
                return absolutePath;
            }

            // Try relative to bin output (if working dir is bin/Debug/net10.0)
            relativePath = Path.Combine("..", "..", "..", "..", "DurableTaskManager", "bin", "Debug", "net10.0", "DurableTaskManager.exe");
            absolutePath = Path.GetFullPath(relativePath);
            
            return absolutePath;
        }
    }
}
