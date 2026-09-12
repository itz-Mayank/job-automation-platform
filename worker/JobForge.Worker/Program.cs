using JobForge.Infrastructure;
using JobForge.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection(WorkerSettings.SectionName));
builder.Services.AddHostedService<ExecutionWorker>();

var app = builder.Build();

// A liveness probe only (process is up), not an execution-health check — the polling
// BackgroundService is what actually does the work. This endpoint exists solely so some
// hosts (e.g. Render's free tier, which only runs "web services" for free) can run the
// worker without paying for a dedicated background-worker plan; it plays no role in
// worker logic itself.
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();
