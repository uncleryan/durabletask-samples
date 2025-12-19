namespace DurableTask.PostgresSQL
{
    using Npgsql;
    using SemVersion;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    class PostgresDbManager
    {
        readonly PostgresOrchestrationServiceSettings settings;
        readonly LogHelper traceHelper;

        public PostgresDbManager(PostgresOrchestrationServiceSettings settings, LogHelper traceHelper)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.traceHelper = traceHelper ?? throw new ArgumentNullException(nameof(traceHelper));
        }

        public async Task CreateOrUpgradeSchemaAsync(bool recreateIfExists)
        {
            // Prevent other create or delete operations from executing at the same time.
            await using DatabaseLock dbLock = await this.AcquireDatabaseLockAsync(this.settings.CreateDatabaseIfNotExists);

            var currentSchemaVersion = new SemanticVersion(0, 0, 0);
            if (recreateIfExists)
            {
                await this.DropSchemaAsync(dbLock);
            }
            else
            {
                // If the database already has the latest schema, then skip
                using NpgsqlCommand command = dbLock.CreateCommand();
                command.CommandText = $"{this.settings.SchemaName}._getversions";
                command.CommandType = CommandType.StoredProcedure;

                try
                {
                    using DbDataReader reader = await PostgresUtils.ExecuteReaderAsync(command, this.traceHelper);
                    if (await reader.ReadAsync())
                    {
                        // The first result contains the latest version
                        currentSchemaVersion = PostgresUtils.GetSemanticVersion(reader);
                        if (currentSchemaVersion >= DTUtils.ExtensionVersion)
                        {
                            // The schema is already up-to-date.
                            return;
                        }
                    }
                }
                catch (PostgresException e) when (e.SqlState == "42883" /* undefined_function */)
                {
                    // Ignore - this is expected for new databases
                }
            }

            // SQL schema setup scripts are embedded resources in the assembly, making them immutable post-build.
            Assembly assembly = typeof(PostgresOrchestrationService).Assembly;
            IEnumerable<string> createSchemaFiles = assembly.GetManifestResourceNames()
                .Where(name => name.Contains(".schema-") && name.EndsWith(".sql"));

            var versionedFiles = new Dictionary<SemanticVersion, string>();
            foreach (string name in createSchemaFiles)
            {
                // Attempt to parse the semver-like string from the resource name.
                // This version number tells us whether to execute the script for this extension version.
                const string RegexExpression = @"schema-(\d+.\d+.\d+(?:-\w+)?).sql$";
                Match match = Regex.Match(name, RegexExpression);
                if (!match.Success || match.Groups.Count < 2)
                {
                    throw new InvalidOperationException($"Failed to find version information in resource name '{name}'. The resource name must match the regex expression '{RegexExpression}'.");
                }

                SemanticVersion version = SemanticVersion.Parse(match.Groups[1].Value);
                if (!versionedFiles.TryAdd(version, match.Value))
                {
                    throw new InvalidOperationException($"There must not be more than one script resource with the same version number! Found {version} multiple times.");
                }
            }

            // Sort by the version numbers to ensure that we run them in the correct order
            foreach ((SemanticVersion version, string name) in versionedFiles.OrderBy(pair => pair.Key))
            {
                // Skip past versions that are already present in the database
                if (version > currentSchemaVersion)
                {
                    await this.ExecuteSqlScriptAsync(name, dbLock);
                    currentSchemaVersion = version;
                }
            }
        }

        async Task<DatabaseLock> AcquireDatabaseLockAsync(bool createDatabaseIfNotExists)
        {
            NpgsqlConnection connection = this.settings.CreateConnection();
            await connection.OpenAsync();

            if (createDatabaseIfNotExists)
            {
                await this.CreateDatabaseIfNotExistsAsync(connection);
            }

            // Use PostgreSQL advisory locks for synchronization
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_lock(hashtext($1))";
            command.Parameters.AddWithValue(this.settings.SchemaName);

            var stopwatch = Stopwatch.StartNew();
            await command.ExecuteNonQueryAsync();

            this.traceHelper.AcquiredAppLock(0, stopwatch);

            return new DatabaseLock(connection, this.settings.SchemaName);
        }

        async Task CreateDatabaseIfNotExistsAsync(NpgsqlConnection connection)
        {
            string databaseName = this.settings.DatabaseName;

            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM pg_database WHERE datname = $1";
            command.Parameters.AddWithValue(databaseName);

            object? result = await command.ExecuteScalarAsync();
            if (result == null)
            {
                // Database doesn't exist, create it
                using NpgsqlCommand createCommand = connection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
                await createCommand.ExecuteNonQueryAsync();
            }
        }

        async Task ExecuteSqlScriptAsync(string resourceName, DatabaseLock dbLock)
        {
            Assembly assembly = typeof(PostgresOrchestrationService).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Failed to load embedded resource '{resourceName}'.");
            }

            using var reader = new StreamReader(stream);
            string script = await reader.ReadToEndAsync();

            // Replace schema name placeholder if it exists
            script = script.Replace("$(SchemaName)", this.settings.SchemaName);

            using NpgsqlCommand command = dbLock.CreateCommand();
            command.CommandText = script;
            command.CommandType = CommandType.Text;

            var stopwatch = Stopwatch.StartNew();
            await command.ExecuteNonQueryAsync();
            stopwatch.Stop();

            this.traceHelper.ExecutedSqlScript(resourceName, stopwatch);
        }

        async Task DropSchemaAsync(DatabaseLock dbLock)
        {
            using NpgsqlCommand command = dbLock.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS {QuoteIdentifier(this.settings.SchemaName)} CASCADE";
            await command.ExecuteNonQueryAsync();
        }

        public async Task DeleteSchemaAsync(bool deleteDatabase)
        {
            await using DatabaseLock dbLock = await this.AcquireDatabaseLockAsync(createDatabaseIfNotExists: false);
            await this.DropSchemaAsync(dbLock);

            if (deleteDatabase)
            {
                using NpgsqlCommand command = dbLock.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(this.settings.DatabaseName)}";
                await command.ExecuteNonQueryAsync();
            }
        }

        static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        class DatabaseLock : IAsyncDisposable
        {
            readonly NpgsqlConnection connection;
            readonly string schemaName;

            public DatabaseLock(NpgsqlConnection connection, string schemaName)
            {
                this.connection = connection;
                this.schemaName = schemaName;
            }

            public NpgsqlCommand CreateCommand() => this.connection.CreateCommand();

            public async ValueTask DisposeAsync()
            {
                // Release the advisory lock
                using NpgsqlCommand command = this.connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(hashtext($1))";
                command.Parameters.AddWithValue(this.schemaName);
                await command.ExecuteNonQueryAsync();

                await this.connection.DisposeAsync();
            }
        }
    }
}
