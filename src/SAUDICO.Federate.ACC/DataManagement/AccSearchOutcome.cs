using System;
using System.Collections.Generic;

namespace SAUDICO.Federate.ACC.DataManagement;

/// <summary>
/// The result of a recursive/entire-project RVT search. Always reflects
/// only successfully searched folders — <see cref="HasPartialFailure"/>
/// tells the caller some folders (e.g. an inaccessible one, 403) were
/// skipped rather than aborting the whole search.
/// </summary>
public sealed class AccSearchOutcome
{
    public IReadOnlyList<AccBrowseNode> Results { get; init; } = Array.Empty<AccBrowseNode>();
    public bool HasPartialFailure { get; init; }
    public int FailedFolderCount { get; init; }
}
