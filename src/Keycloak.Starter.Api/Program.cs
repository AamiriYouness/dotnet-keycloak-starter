using Keycloak.Starter.Authentication;
using Keycloak.Starter.Api.Configuration;
using Keycloak.Starter.Api.Delegation;
using Keycloak.Starter.Api.Endpoints;
using Keycloak.Starter.Api.OpenApi;
using Keycloak.Starter.Api.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("/run/secrets/appsettings.json", optional: true, reloadOnChange: false);
builder.Services.AddTrustedReverseProxy(builder.Configuration);
builder.Services.AddApiOptions(builder.Configuration, builder.Environment);
builder.Services.AddKeycloakApiAuthentication();
builder.Services.AddTokenExchange();
builder.Services.AddApiOpenApi();

var app = builder.Build();
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapApiDocumentation();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapApiEndpoints();
app.Run();

public partial class Program;
