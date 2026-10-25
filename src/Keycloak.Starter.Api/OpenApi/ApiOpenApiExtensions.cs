using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace Keycloak.Starter.Api.OpenApi;

public static class ApiOpenApiExtensions
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
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
        return services;
    }

    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Keycloak Starter API v1");
            options.DocumentTitle = "Keycloak Starter API";
        });
        return app;
    }
}
