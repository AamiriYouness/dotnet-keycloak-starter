namespace Keycloak.Starter.EndToEndTests;

// Docker bridge creation/removal changes host interfaces on Linux. Keep that work
// outside the lifetime of a browser running against another fixture.
[CollectionDefinition(BrowserStackCollection.Name, DisableParallelization = true)]
public sealed class BrowserStackCollection
{
    public const string Name = "Docker browser stacks";
}
