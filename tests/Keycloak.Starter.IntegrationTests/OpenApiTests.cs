using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Keycloak.Starter.IntegrationTests;

public sealed class OpenApiTests
{
    [Fact]
    public async Task Document_defines_http_bearer_authentication()
    {
        using var document = await ReadDocumentAsync(TestContext.Current.CancellationToken);

        var bearer = document.RootElement.GetProperty("components")
            .GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
    }

    [Theory]
    [InlineData("/me", false)]
    [InlineData("/reader", true)]
    [InlineData("/admin", true)]
    public async Task Protected_operation_requires_bearer_and_documents_failures(string path, bool roleRequired)
    {
        using var document = await ReadDocumentAsync(TestContext.Current.CancellationToken);

        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");
        var security = operation.GetProperty("security");
        Assert.Equal(1, security.GetArrayLength());
        Assert.Equal(0, security[0].GetProperty("Bearer").GetArrayLength());
        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("401", out _));
        Assert.Equal(roleRequired, responses.TryGetProperty("403", out _));
    }

    [Fact]
    public async Task Health_is_documented_without_an_authentication_requirement()
    {
        using var document = await ReadDocumentAsync(TestContext.Current.CancellationToken);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("security", out var global) && global.GetArrayLength() > 0);
        var operation = root.GetProperty("paths").GetProperty("/health").GetProperty("get");
        Assert.False(operation.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
