using Keycloak.Starter.Worker;
using Keycloak.Starter.Worker.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWorkerOptions(builder.Configuration, builder.Environment);
builder.Services.AddWorkerAuthentication();
builder.Services.AddHostedService<SynchronizationWorker>();
await builder.Build().RunAsync();

public partial class Program;
