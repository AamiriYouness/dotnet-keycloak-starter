namespace Keycloak.Starter.Authentication;

public sealed class ReverseProxySettings
{
    public bool Enabled { get; set; }
    public string[] KnownProxies { get; set; } = [];
}
