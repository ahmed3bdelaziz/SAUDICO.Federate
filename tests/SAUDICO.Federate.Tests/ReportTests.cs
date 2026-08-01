using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Export;
using SAUDICO.Federate.Shared;
using Xunit;

namespace SAUDICO.Federate.Tests;

public sealed class ReportTests : IDisposable
{
    private readonly string directory;

    public ReportTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "SAUDICO-Federate-Report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static Job MakeJob(string source, DateTime startUtc)
    {
        return new Job
        {
            Source = source,
            OutputFolder = "C:\\Out",
            Kind = ModelKind.Local,
            State = State.Succeeded,
            Start = startUtc,
            End = startUtc.AddSeconds(5),
            Settings = ExportSettings.Default()
        };
    }

    [Fact]
    public void Csv_EscapesCommasAndQuotes()
    {
        Job job = MakeJob("C:\\Models\\Tower, Phase \"1\".rvt", DateTime.UtcNow);
        job.Warnings = "Note, with a \"quote\"";

        string path = Report.Write(directory, new List<Job> { job });
        string content = File.ReadAllText(path);

        Assert.Contains("\"C:\\Models\\Tower, Phase \"\"1\"\".rvt\"", content);
        Assert.Contains("\"Note, with a \"\"quote\"\"\"", content);
    }

    [Fact]
    public void Csv_WritesIntegrityFields()
    {
        Job job = MakeJob("C:\\Models\\A.rvt", DateTime.UtcNow);
        DateTime writeUtc = DateTime.UtcNow;

        job.IntegrityBefore = new SourceFileSnapshot { Path = job.Source, Exists = true, Length = 100, LastWriteTimeUtc = writeUtc, IsReadOnly = false, CapturedAtUtc = writeUtc };
        job.IntegrityAfter = new SourceFileSnapshot { Path = job.Source, Exists = true, Length = 100, LastWriteTimeUtc = writeUtc, IsReadOnly = false, CapturedAtUtc = writeUtc };
        job.IntegrityStatus = SourceIntegrityStatus.Pass;
        job.IntegrityMessage = "Source integrity verified.";

        string path = Report.Write(directory, new List<Job> { job });
        string content = File.ReadAllText(path);

        Assert.Contains("Source Integrity Status", content);
        Assert.Contains("Pass", content);
        Assert.Contains("Source integrity verified.", content);
        Assert.Contains("100", content);
    }

    [Fact]
    public void Csv_WritesIsoUtcTimestamps()
    {
        DateTime writeUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Job job = MakeJob("C:\\Models\\B.rvt", DateTime.UtcNow);
        job.IntegrityBefore = new SourceFileSnapshot { Path = job.Source, Exists = true, Length = 1, LastWriteTimeUtc = writeUtc, IsReadOnly = false, CapturedAtUtc = writeUtc };

        string path = Report.Write(directory, new List<Job> { job });
        string content = File.ReadAllText(path);
        string expected = writeUtc.ToString("O", CultureInfo.InvariantCulture);

        Assert.Contains(expected, content);
        Assert.EndsWith("Z", expected);
    }

    [Fact]
    public void Csv_MissingSnapshotFieldsAreBlankNotFabricated()
    {
        Job job = MakeJob("C:\\Models\\C.rvt", DateTime.UtcNow);

        string path = Report.Write(directory, new List<Job> { job });
        string content = File.ReadAllText(path);
        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("\"\",\"\",\"\",\"\"", lines[1]);
    }

    [Fact]
    public void Csv_AccJob_ReportsNotApplicableCloudSource_AndNeverInventsFileMetadata()
    {
        Job job = MakeJob("Architectural Model.rvt", DateTime.UtcNow);
        job.Kind = ModelKind.Acc;
        job.AccSource = new AccCloudSource
        {
            ItemId = "item-1",
            Region = "US",
            ProjectGuid = Guid.NewGuid().ToString(),
            ModelGuid = Guid.NewGuid().ToString(),
            ResolutionStatus = AccSourceResolutionStatus.CloudModelVerified,
        };

        // Exactly what Engine.Run assigns for an ACC job: no before/after
        // snapshot is ever captured, so nothing can be fabricated for it.
        job.IntegrityBefore = null;
        job.IntegrityAfter = null;
        job.IntegrityStatus = SourceIntegrityStatus.NotApplicableCloudSource;
        job.IntegrityMessage = "Source integrity checks are not applicable to ACC cloud-hosted sources.";

        string path = Report.Write(directory, new List<Job> { job });
        string content = File.ReadAllText(path);
        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("NotApplicableCloudSource", content);
        Assert.Contains("not applicable to ACC cloud-hosted sources", content);
        // All eight before/after file-metadata columns must be blank.
        Assert.Contains("\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\"", lines[1]);
    }
}
