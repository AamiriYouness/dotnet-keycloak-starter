using Keycloak.Starter.Transactions.Configuration;
using Keycloak.Starter.Transactions.Endpoints;
using Keycloak.Starter.Transactions.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTransactionsOptions(builder.Configuration, builder.Environment);
builder.Services.AddTransactionsAuthentication();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapTransactionsEndpoints();
app.Run();

public partial class Program;
