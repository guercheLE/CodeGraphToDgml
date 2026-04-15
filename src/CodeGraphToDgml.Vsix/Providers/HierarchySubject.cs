namespace CodeGraphToDgml.Vsix.Providers;

internal sealed class HierarchySubject
{
    public HierarchySubject(string providerId, string displayName, object payload)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        Payload = payload;
    }

    public string ProviderId { get; }

    public string DisplayName { get; }

    public object Payload { get; }
}
