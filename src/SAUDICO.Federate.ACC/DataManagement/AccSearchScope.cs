namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>Which folders an RVT search covers.</summary>
public enum AccSearchScope
{
    /// <summary>The currently open folder only — local in-memory filter, no additional APS request.</summary>
    CurrentFolder,

    /// <summary>The currently open folder and all of its subfolders — one recursive Data Management search request (plus pagination).</summary>
    CurrentFolderAndSubfolders,

    /// <summary>Every top folder of the current project, searched recursively and merged.</summary>
    EntireProject,
}
