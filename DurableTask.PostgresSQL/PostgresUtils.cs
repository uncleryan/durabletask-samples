namespace DurableTask.PostgresSQL
{
    using Npgsql;
    using SemVersion;
    using System;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Threading.Tasks;

    static class PostgresUtils
    {
        static readonly Random random = new Random();
        static readonly char[] TraceContextSeparators = new char[] { '\n' };

        public static string? GetStringOrNull(this DbDataReader reader, int columnIndex)
        {
            return reader.IsDBNull(columnIndex) ? null : reader.GetString(columnIndex);
        }

        public static SemanticVersion GetSemanticVersion(DbDataReader reader)
        {
            int major = reader.GetInt32(reader.GetOrdinal("Major"));
            int minor = reader.GetInt32(reader.GetOrdinal("Minor"));
            int patch = reader.GetInt32(reader.GetOrdinal("Patch"));
            string? prerelease = reader.GetStringOrNull(reader.GetOrdinal("Prerelease"));

            if (string.IsNullOrEmpty(prerelease))
            {
                return new SemanticVersion(major, minor, patch);
            }
            else
            {
                return new SemanticVersion(major, minor, patch, prerelease);
            }
        }

        public static async Task<DbDataReader> ExecuteReaderAsync(
            NpgsqlCommand command,
            LogHelper traceHelper,
            string? instanceId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            int retryCount = 0;

            while (true)
            {
                try
                {
                    DbDataReader reader = await command.ExecuteReaderAsync();
                    traceHelper.SprocCompleted(command.CommandText, stopwatch, retryCount, instanceId);
                    return reader;
                }
                catch (PostgresException e) when (IsTransientError(e))
                {
                    retryCount++;
                    traceHelper.TransientDatabaseFailure(e, instanceId, retryCount);

                    if (retryCount >= 3)
                    {
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                }
            }
        }

        public static async Task<int> ExecuteNonQueryAsync(
            NpgsqlCommand command,
            LogHelper traceHelper,
            string? instanceId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            int retryCount = 0;

            while (true)
            {
                try
                {
                    int result = await command.ExecuteNonQueryAsync();
                    traceHelper.SprocCompleted(command.CommandText, stopwatch, retryCount, instanceId);
                    return result;
                }
                catch (PostgresException e) when (IsTransientError(e))
                {
                    retryCount++;
                    traceHelper.TransientDatabaseFailure(e, instanceId, retryCount);

                    if (retryCount >= 3)
                    {
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                }
            }
        }

        public static async Task<object?> ExecuteScalarAsync(
            NpgsqlCommand command,
            LogHelper traceHelper,
            string? instanceId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            int retryCount = 0;

            while (true)
            {
                try
                {
                    object? result = await command.ExecuteScalarAsync();
                    traceHelper.SprocCompleted(command.CommandText, stopwatch, retryCount, instanceId);
                    return result;
                }
                catch (PostgresException e) when (IsTransientError(e))
                {
                    retryCount++;
                    traceHelper.TransientDatabaseFailure(e, instanceId, retryCount);

                    if (retryCount >= 3)
                    {
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                }
            }
        }

        static bool IsTransientError(PostgresException exception)
        {
            // PostgreSQL transient error codes
            // See: https://www.postgresql.org/docs/current/errcodes-appendix.html
            return exception.SqlState switch
            {
                "08000" => true, // connection_exception
                "08003" => true, // connection_does_not_exist
                "08006" => true, // connection_failure
                "08001" => true, // sqlclient_unable_to_establish_sqlconnection
                "08004" => true, // sqlserver_rejected_establishment_of_sqlconnection
                "57P03" => true, // cannot_connect_now
                "53300" => true, // too_many_connections
                "40001" => true, // serialization_failure
                "40P01" => true, // deadlock_detected
                _ => false
            };
        }

        public static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        public static NpgsqlParameter AddParameter(this NpgsqlCommand command, string name, NpgsqlTypes.NpgsqlDbType type, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.NpgsqlDbType = type;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
            return parameter;
        }
    }
}
