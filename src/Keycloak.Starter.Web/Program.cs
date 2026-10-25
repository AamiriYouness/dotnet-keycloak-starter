using Keycloak.Starter.Authentication;
using Keycloak.Starter.Web.Configuration;
using Keycloak.Starter.Web.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("/run/secrets/appsettings.json", optional: true, reloadOnChange: false);
builder.Services.AddTrustedReverseProxy(builder.Configuration);
builder.Services.AddWebOptions(builder.Configuration, builder.Environment);
builder.Services.AddPersistentAuthenticationStorage();
builder.Services.AddKeycloakWebAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    foreach (var page in new[] { "/Index", "/SignedOut", "/Error", "/Account/Login" })
    {
        options.Conventions.AllowAnonymousToPage(page);
    }
});

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Run();
