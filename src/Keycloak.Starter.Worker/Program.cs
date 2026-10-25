using Keycloak.Starter.Worker;
using Keycloak.Starter.Worker.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("/run/secrets/appsettings.json", optional: true, reloadOnChange: false);
builder.Services.AddWorkerOptions(builder.Configuration, builder.Environment);
builder.Services.AddWorkerAuthentication();
builder.Services.AddHostedService<SynchronizationWorker>();
await builder.Build().RunAsync();

public partial class Program;
