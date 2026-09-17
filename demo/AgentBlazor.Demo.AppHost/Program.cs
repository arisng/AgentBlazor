var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithContainerName("agentblazor-demo-sqlserver")
    .WithDataVolume("agentblazor-demo-sqlserver");

var demoDb = sql.AddDatabase("demo-db");

builder.AddProject<Projects.AgentBlazor_Demo>("demo")
    .WithReference(demoDb)
    .WaitFor(demoDb);

builder.Build().Run();
