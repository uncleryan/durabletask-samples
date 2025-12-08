using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlServer("durableSql")
    .WithDataVolume("durableDb-volume")
    //.WithDataBindMount("./path/to/local/folder")
    .AddDatabase("durableDb");

// Launch DurableTaskClient as an executable with its own interactive console window
//var clientProjectPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "DurableTaskClient"));

//builder.AddExecutable("durableClient", "dotnet", clientProjectPath, "run")
//    .WithReference(db)
//    .WaitFor(db);

builder.AddProject<DurableTaskWorker>("durableWorker")
.WithReference(db)
.WaitFor(db);

builder.AddProject<DurableTaskManager>("durableManager")
.WithReference(db)
.WaitFor(db);

builder.Build().Run();

