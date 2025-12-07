using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlServer("durableSql").AddDatabase("durableDb");

builder.AddProject<DurableTaskWorker>("durableworker")
   .WithReference(db)
   .WaitForStart(db);

builder.AddProject<DurableTaskClient>("durableclient")
    .WithReference(db)
    .WaitForStart(db);

builder.Build().Run();
