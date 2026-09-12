using JobForge.Infrastructure;
using JobForge.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection(WorkerSettings.SectionName));
builder.Services.AddHostedService<ExecutionWorker>();

var host = builder.Build();
host.Run();
