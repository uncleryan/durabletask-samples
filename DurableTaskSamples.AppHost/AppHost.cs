using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlServer("durableSql").AddDatabase("durableDb");

builder.AddProject<DurableTaskClient>("durableClient")
.WithReference(db)
.WaitFor(db)
.ExcludeFromManifest();

builder.AddProject<DurableTaskWorker>("durableWorker")
.WithReference(db)
.WaitFor(db);

builder.AddProject<DurableTaskManager>("durableManager")
.WithReference(db)
.WaitFor(db);

builder.Build().Run();
