using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlServer("durableSql").AddDatabase("durableDb");

builder.AddProject<DurableTaskManager>("durableManager")
   .WithReference(db)
   .WaitForStart(db);

builder.AddProject<DurableTaskWorker>("durableWorker")
   .WithReference(db)
   .WaitForStart(db);

builder.AddProject<DurableTaskClient>("durableClient")
    .WithReference(db)
    .WaitForStart(db);

builder.Build().Run();
