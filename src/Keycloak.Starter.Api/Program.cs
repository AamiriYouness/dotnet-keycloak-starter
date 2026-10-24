using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Keycloak Starter API";
        document.Info.Version = "v1";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste a Keycloak access token only. Swagger UI adds the Bearer prefix."
        };
        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var authorization = metadata.OfType<IAuthorizeData>().ToArray();
        if (authorization.Length == 0 || metadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid access token." });
        if (authorization.Any(data => !string.IsNullOrWhiteSpace(data.Policy) || !string.IsNullOrWhiteSpace(data.Roles)))
        {
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "The caller lacks the required API role." });
        }
        return Task.CompletedTask;
    });
});

var authority = builder.Configuration["Keycloak:Authority"]
    ?? throw new InvalidOperationException("Keycloak:Authority is required.");
var audience = builder.Configuration["Keycloak:Audience"]
    ?? throw new InvalidOperationException("Keycloak:Audience is required.");
var metadataAddress = builder.Configuration["Keycloak:MetadataAddress"];

if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
    (authorityUri.Scheme != Uri.UriSchemeHttp && authorityUri.Scheme != Uri.UriSchemeHttps) ||
    (!builder.Environment.IsDevelopment() && authorityUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException("Keycloak:Authority must be an absolute HTTPS URL (HTTP is allowed in Development).");
}

if (string.IsNullOrWhiteSpace(audience))
{
    throw new InvalidOperationException("Keycloak:Audience must not be empty.");
}

if (!string.IsNullOrWhiteSpace(metadataAddress) &&
    (!Uri.TryCreate(metadataAddress, UriKind.Absolute, out var metadataUri) ||
     (metadataUri.Scheme != Uri.UriSchemeHttp && metadataUri.Scheme != Uri.UriSchemeHttps) ||
     (!builder.Environment.IsDevelopment() && metadataUri.Scheme != Uri.UriSchemeHttps)))
{
    throw new InvalidOperationException("Keycloak:MetadataAddress must be an absolute HTTPS URL (HTTP is allowed in Development).");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
        }
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.IncludeErrorDetails = builder.Environment.IsDevelopment();
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                {
                    return Task.CompletedTask;
                }

                var resourceAccess = context.Principal.FindFirst("resource_access")?.Value;
                if (string.IsNullOrWhiteSpace(resourceAccess))
                {
                    return Task.CompletedTask;
                }

                try
                {
                    using var document = JsonDocument.Parse(resourceAccess);
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty(audience, out var client) ||
                        client.ValueKind != JsonValueKind.Object ||
                        !client.TryGetProperty("roles", out var roles) ||
                        roles.ValueKind != JsonValueKind.Array)
                    {
                        return Task.CompletedTask;
                    }

                    foreach (var role in roles.EnumerateArray())
                    {
                        if (role.ValueKind == JsonValueKind.String &&
                            role.GetString() is { Length: > 0 } name)
                        {
                            identity.AddClaim(new Claim(identity.RoleClaimType, name));
                        }
                    }
                }
                catch (JsonException)
                {
                    // A malformed roles claim grants no roles.
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanRead", policy => policy.RequireAuthenticatedUser().RequireRole("reader"));
    options.AddPolicy("CanAdmin", policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Keycloak Starter API v1");
        options.DocumentTitle = "Keycloak Starter API";
    });
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "Ok!")
    .AllowAnonymous();

app.MapGet("/me", (HttpContext context) => Results.Ok(new { sub = context.User.FindFirst("sub")?.Value }))
    .WithSummary("Return the authenticated caller's subject")
    .RequireAuthorization();

app.MapGet("/reader", () => Results.Ok(new { message = "Reader access granted" }))
    .WithSummary("Requires the starter-api reader role")
    .RequireAuthorization("CanRead");

app.MapGet("/admin", () => Results.Ok(new { message = "Admin access granted" }))
    .WithSummary("Requires the starter-api admin role")
    .RequireAuthorization("CanAdmin");

app.Run();

public partial class Program;
