namespace CallHierarchyToDgml.Vsix;

internal sealed partial class PackageGuids
{
    public const string CallHierarchyToDgmlString = "8bd0ea2c-2cc7-4466-a5d0-e19b4fd86b43";
    public static readonly Guid CallHierarchyToDgml = new(CallHierarchyToDgmlString);

    public const string EditorCommandsString = "eb6634f9-47c6-4ebd-9814-c5a182960783";
    public static readonly Guid EditorCommands = new(EditorCommandsString);
}

internal sealed partial class PackageIds
{
    public const int TraverseUpToDgmlCommand = 0x0100;
    public const int EditorContextMenuGroup = 0x1020;
}
