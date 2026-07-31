namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// What an <see cref="AccBrowseNode"/> represents in the read-only ACC
/// browser. <see cref="Folder"/> and <see cref="RvtFile"/> are navigable/
/// selectable leaves returned from folder-contents listings; a folder is
/// always navigable, an RVT file is always a selectable leaf.
/// </summary>
public enum AccNodeKind
{
    Hub,
    Project,
    Folder,
    RvtFile,
}
