# Quick Start Guide - DurableTask.PostgresSQL

## Prerequisites

- PostgreSQL 12 or higher (14+ recommended)
- .NET 10.0 SDK
- Npgsql 9.0.2 (installed via NuGet)

## Step 1: Setup PostgreSQL Database

### Install PostgreSQL

**Windows:**
```bash
# Download installer from https://www.postgresql.org/download/windows/
# Or use Chocolatey:
choco install postgresql
```

**macOS:**
```bash
brew install postgresql@14
brew services start postgresql@14
```

**Linux (Ubuntu/Debian):**
```bash
sudo apt-get update
sudo apt-get install postgresql postgresql-contrib
sudo systemctl start postgresql
```

### Create Database

```bash
# Connect as postgres user
psql -U postgres

# Create database
CREATE DATABASE durabletask;

# Create user (optional)
CREATE USER dtuser WITH PASSWORD 'yourpassword';
GRANT ALL PRIVILEGES ON DATABASE durabletask TO dtuser;

# Exit
\q
```

## Step 2: Deploy Database Schema

### Option A: Using psql

```bash
# Connect to the database
psql -U postgres -d durabletask

# Run schema script
\i DurableTask.PostgresSQL/Scripts/schema-1.0.0.sql

# Run logic scripts
\i DurableTask.PostgresSQL/Scripts/logic-1.0.0.sql
\i DurableTask.PostgresSQL/Scripts/logic-1.0.0-part2.sql
\i DurableTask.PostgresSQL/Scripts/logic-1.0.0-part3.sql
\i DurableTask.PostgresSQL/Scripts/logic-1.0.0-part4.sql

# Verify installation
SELECT routine_name FROM information_schema.routines WHERE routine_schema = 'dt';
```

### Option B: Using Code

```csharp
var settings = new PostgresOrchestrationServiceSettings(
    connectionString: "Host=localhost;Database=durabletask;Username=postgres;Password=yourpassword",
    taskHubName: "MyTaskHub");

var service = new PostgresOrchestrationService(settings);
await service.CreateIfNotExistsAsync(); // Creates schema automatically
```

## Step 3: Create Your First Orchestration

### Define an Orchestration

```csharp
using DurableTask.Core;

public class HelloOrchestration : TaskOrchestration<string, string>
{
    public override async Task<string> RunTask(OrchestrationContext context, string input)
    {
        var result = await context.ScheduleTask<string>(typeof(SayHelloActivity), input);
        return result;
    }
}
```

### Define an Activity

```csharp
using DurableTask.Core;

public class SayHelloActivity : TaskActivity<string, string>
{
    protected override string Execute(TaskContext context, string input)
    {
        return $"Hello, {input}!";
    }
}
```

## Step 4: Setup the Worker

```csharp
using DurableTask.Core;
using DurableTask.PostgresSQL;

class Program
{
    static async Task Main(string[] args)
    {
        // Configure the service
        var settings = new PostgresOrchestrationServiceSettings(
            connectionString: "Host=localhost;Database=durabletask;Username=postgres;Password=yourpassword",
            taskHubName: "MyTaskHub");

        // Optional: Adjust concurrency settings
        settings.MaxConcurrentActivities = 20;
        settings.MaxActiveOrchestrations = 10;

        // Create the service
        var service = new PostgresOrchestrationService(settings);
        
        // Ensure schema exists
        await service.CreateIfNotExistsAsync();

        // Create and configure worker
        var worker = new TaskHubWorker(service);
        
        // Register orchestrations and activities
        worker.AddTaskOrchestrations(typeof(HelloOrchestration));
        worker.AddTaskActivities(typeof(SayHelloActivity));

        // Start the worker
        await worker.StartAsync();
        
        Console.WriteLine("Worker started. Press any key to stop...");
        Console.ReadKey();
        
        // Stop the worker
        await worker.StopAsync();
    }
}
```

## Step 5: Create and Monitor Orchestration Instances

### Create a Client

```csharp
using DurableTask.Core;
using DurableTask.PostgresSQL;

// Create the service
var settings = new PostgresOrchestrationServiceSettings(
    connectionString: "Host=localhost;Database=durabletask;Username=postgres;Password=yourpassword",
    taskHubName: "MyTaskHub");

var service = new PostgresOrchestrationService(settings);

// Create a client
var client = new TaskHubClient(service);

// Start an orchestration
var instance = await client.CreateOrchestrationInstanceAsync(
    typeof(HelloOrchestration),
    instanceId: Guid.NewGuid().ToString(),
    input: "World");

Console.WriteLine($"Created instance: {instance.InstanceId}");

// Wait for completion
var state = await client.WaitForOrchestrationAsync(
    instance,
    timeout: TimeSpan.FromMinutes(5));

Console.WriteLine($"Result: {state.Output}");
Console.WriteLine($"Status: {state.OrchestrationStatus}");
```

## Step 6: Query Orchestration State

### Query Single Instance

```csharp
var state = await client.GetOrchestrationStateAsync(instanceId);
if (state != null)
{
    Console.WriteLine($"Status: {state.OrchestrationStatus}");
    Console.WriteLine($"Created: {state.CreatedTime}");
    Console.WriteLine($"Output: {state.Output}");
}
```

### Query Multiple Instances

```csharp
var query = new OrchestrationQuery()
{
    RuntimeStatus = new[]
    {
        OrchestrationStatus.Running,
        OrchestrationStatus.Pending
    },
    PageSize = 100
};

var result = await client.GetOrchestrationStateAsync(query);
foreach (var state in result.OrchestrationState)
{
    Console.WriteLine($"{state.InstanceId}: {state.OrchestrationStatus}");
}
```

## Step 7: Advanced Features

### Send External Events

```csharp
await client.RaiseEventAsync(instance, "ApprovalReceived", "approved");
```

### Terminate Orchestration

```csharp
await client.TerminateInstanceAsync(instance, "User requested cancellation");
```

### Purge Old Instances

```csharp
var purgeFilter = new PurgeInstanceFilter(
    DateTime.UtcNow.AddDays(-30), // Created before
    DateTime.UtcNow,               // Created after
    new[] { OrchestrationStatus.Completed, OrchestrationStatus.Failed });

var result = await client.PurgeInstanceStateAsync(purgeFilter);
Console.WriteLine($"Purged {result.InstancesDeleted} instances");
```

## Configuration Options

### Connection String Options

```csharp
// Basic connection string
"Host=localhost;Database=durabletask;Username=postgres;Password=yourpassword"

// With connection pooling
"Host=localhost;Database=durabletask;Username=postgres;Password=yourpassword;Pooling=true;MinPoolSize=5;MaxPoolSize=100"

// With SSL
"Host=localhost;Database=durabletask;Username=postgres;Password=yourpassword;SslMode=Require"

// Azure Database for PostgreSQL
"Host=myserver.postgres.database.azure.com;Database=durabletask;Username=myuser@myserver;Password=yourpassword;SslMode=Require"
```

### Settings Options

```csharp
var settings = new PostgresOrchestrationServiceSettings(
    connectionString: "...",
    taskHubName: "MyTaskHub",
    schemaName: "dt") // Optional, defaults to "dt"
{
    // Concurrency
    MaxConcurrentActivities = 20,
    MaxActiveOrchestrations = 10,
    
    // Polling intervals
    MinOrchestrationPollingInterval = TimeSpan.FromMilliseconds(50),
    MaxOrchestrationPollingInterval = TimeSpan.FromSeconds(3),
    MinActivityPollingInterval = TimeSpan.FromMilliseconds(50),
    MaxActivityPollingInterval = TimeSpan.FromSeconds(3),
    
    // Work item settings
    WorkItemBatchSize = 10,
    WorkItemLockTimeout = TimeSpan.FromMinutes(2),
    
    // Database creation
    CreateDatabaseIfNotExists = false,
    
    // Logging
    LogReplayEvents = false
};
```

## Monitoring and Troubleshooting

### Check Database Status

```sql
-- Check if schema exists
SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'dt';

-- Check tables
SELECT table_name FROM information_schema.tables WHERE table_schema = 'dt';

-- Check functions
SELECT routine_name, routine_type 
FROM information_schema.routines 
WHERE routine_schema = 'dt' 
ORDER BY routine_name;

-- Check active instances
SELECT * FROM dt.vInstances WHERE RuntimeStatus IN ('Pending', 'Running');

-- Check pending events
SELECT COUNT(*) FROM dt.NewEvents;

-- Check pending tasks
SELECT COUNT(*) FROM dt.NewTasks;
```

### Enable Query Logging

```sql
-- Enable query logging
ALTER DATABASE durabletask SET log_statement = 'all';

-- View slow queries
SELECT * FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 10;
```

### Common Issues

#### Issue: "The instance does not exist"
- Check that the instance was created successfully
- Verify the TaskHub name matches

#### Issue: Worker not processing work items
- Check that the worker is started
- Verify database connectivity
- Check for lock expiration issues
- Verify the schema is properly deployed

#### Issue: Deadlocks
- PostgreSQL has automatic deadlock detection
- Check `pg_locks` table for lock conflicts
- Increase lock timeout if needed

## Docker Setup (Optional)

### Using Docker Compose

```yaml
version: '3.8'
services:
  postgres:
    image: postgres:14
    environment:
      POSTGRES_DB: durabletask
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: yourpassword
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./Scripts:/docker-entrypoint-initdb.d

volumes:
  postgres-data:
```

Start with:
```bash
docker-compose up -d
```

## Production Considerations

### Connection Pooling
- Enable connection pooling in Npgsql
- Set appropriate pool sizes (Min: 5, Max: 100)
- Monitor connection usage

### Performance
- Add indexes for custom query patterns
- Use partial indexes where appropriate
- Monitor query performance with `pg_stat_statements`
- Consider connection pooling with PgBouncer

### High Availability
- Use PostgreSQL replication
- Configure automatic failover
- Use connection string with multiple hosts

### Backup
```bash
# Backup database
pg_dump -U postgres -d durabletask > backup.sql

# Restore database
psql -U postgres -d durabletask < backup.sql
```

### Monitoring
- Use `pg_stat_activity` for active queries
- Monitor table sizes and growth
- Set up alerts for lock timeouts
- Monitor worker health and throughput

## Next Steps

1. Review the [README.md](README.md) for complete documentation
2. Check [ARCHITECTURE.md](ARCHITECTURE.md) for architecture details
3. Read [Scripts/README.md](Scripts/README.md) for SQL documentation
4. Implement remaining service methods (see TODO comments)
5. Write integration tests
6. Deploy to production

## Support

- **GitHub Issues**: Report bugs and request features
- **Documentation**: See README.md files
- **PostgreSQL Docs**: https://www.postgresql.org/docs/
- **Npgsql Docs**: https://www.npgsql.org/doc/
- **Durable Task Framework**: https://github.com/Azure/durabletask

---

**Happy Orchestrating with PostgreSQL! ????**
