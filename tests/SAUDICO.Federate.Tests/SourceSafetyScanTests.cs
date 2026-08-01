using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SAUDICO.Federate.Tests;

/// <summary>
/// Executable version of the manual "forbidden-write re-scan" that every
/// prior audit performed by hand. Scans the real src tree for any Revit
/// document write/sync/publish/relinquish call, so a future change that
/// introduces one fails the build instead of silently shipping.
/// </summary>
public sealed class SourceSafetyScanTests
{
    private static string SrcDirectory()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

    [Theory]
    [InlineData(".SaveCloudModel(")]
    [InlineData(".SaveAsCloudModel(")]
    [InlineData(".SynchronizeWithCentral(")]
    [InlineData("SynchronizeWithCentralOptions")]
    [InlineData("TransactWithCentralOptions")]
    [InlineData("RelinquishOptions")]
    [InlineData("WorksharingUtils.RelinquishOwnership")]
    [InlineData("OpenAndActivateDocument(")]
    public void NoForbiddenRevitWriteApiIsCalledAnywhereInSrc(string forbidden)
    {
        List<string> offenders = SourceFiles()
            .Where(path => File.ReadAllText(path).Contains(forbidden, StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Forbidden Revit API usage '{forbidden}' found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoRevitDocumentSaveOrSaveAsCallExistsInSrc()
    {
        // ".Save(" / ".SaveAs(" would also match the local settings/token
        // stores, which are app-config files and explicitly allowed — so
        // this narrows to the Revit-document forms, which always take a
        // SaveOptions/SaveAsOptions argument.
        string[] forbidden = { "SaveOptions", "SaveAsOptions" };

        List<string> offenders = SourceFiles()
            .Where(path => forbidden.Any(f => File.ReadAllText(path).Contains(f, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Revit document Save/SaveAs usage found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryDocumentCloseInSrcPassesFalse()
    {
        // Deliberately receiver-aware: src also contains legitimate
        // non-Revit Close() calls (an HttpListener response stream, a WPF
        // Window) which must not be flagged. Only receivers named like a
        // Revit Document are checked.
        string[] documentReceivers = { "document.Close(", "doc.Close(", "Document.Close(" };

        List<string> offenders = SourceFiles()
            .Where(path =>
            {
                string text = File.ReadAllText(path);

                foreach (string receiver in documentReceivers)
                {
                    int index = 0;
                    while ((index = text.IndexOf(receiver, index, StringComparison.Ordinal)) >= 0)
                    {
                        if (!text.Substring(index).StartsWith(receiver.Replace("Close(", "Close(false)"), StringComparison.Ordinal))
                        {
                            return true;
                        }

                        index += receiver.Length;
                    }
                }

                return false;
            })
            .ToList();

        Assert.True(offenders.Count == 0,
            $"A Document.Close call that does not pass false was found in: {string.Join(", ", offenders)}");
    }
}
