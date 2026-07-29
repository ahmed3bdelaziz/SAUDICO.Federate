using System;
using System.IO;
using SAUDICO.Federate.Core;
using Xunit;

namespace SAUDICO.Federate.Tests;

public sealed class SourceIntegrityValidatorTests : IDisposable
{
    private readonly string directory;
    private readonly ISourceIntegrityValidator validator = new SourceIntegrityValidator();

    public SourceIntegrityValidatorTests()
    {
        directory = Path.Combine(Path.GetTempPath(), "SAUDICO-Federate-Tests-" + Guid.NewGuid().ToString("N"));
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

    private string CreateFile(string content = "source")
    {
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".rvt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void UnchangedFile_Passes()
    {
        string path = CreateFile();
        SourceFileSnapshot before = validator.Capture(path);
        SourceFileSnapshot after = validator.Capture(path);

        SourceIntegrityValidationResult result = validator.Compare(before, after);

        Assert.Equal(SourceIntegrityStatus.Pass, result.Status);
    }

    [Fact]
    public void ChangedFileSize_Fails()
    {
        string path = CreateFile("short");
        SourceFileSnapshot before = validator.Capture(path);

        File.WriteAllText(path, "a much longer replacement body");
        SourceFileSnapshot after = validator.Capture(path);

        SourceIntegrityValidationResult result = validator.Compare(before, after);

        Assert.Equal(SourceIntegrityStatus.Fail, result.Status);
        Assert.Contains("Source Integrity Violation", result.Message);
    }

    [Fact]
    public void ChangedLastWriteTimeUtc_Fails()
    {
        string path = CreateFile();
        SourceFileSnapshot before = validator.Capture(path);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        SourceFileSnapshot after = validator.Capture(path);

        SourceIntegrityValidationResult result = validator.Compare(before, after);

        Assert.Equal(SourceIntegrityStatus.Fail, result.Status);
    }

    [Fact]
    public void DeletedFile_Fails()
    {
        string path = CreateFile();
        SourceFileSnapshot before = validator.Capture(path);

        File.Delete(path);
        SourceFileSnapshot after = validator.Capture(path);

        SourceIntegrityValidationResult result = validator.Compare(before, after);

        Assert.Equal(SourceIntegrityStatus.Fail, result.Status);
    }

    [Fact]
    public void MissingPreSource_Fails()
    {
        string path = Path.Combine(directory, "never-existed.rvt");
        SourceFileSnapshot before = validator.Capture(path);
        SourceFileSnapshot after = validator.Capture(path);

        SourceIntegrityValidationResult result = validator.Compare(before, after);

        Assert.Equal(SourceIntegrityStatus.Fail, result.Status);
    }

    [Fact]
    public void ReadOnlyAttributeChange_ReportsWarning()
    {
        string path = CreateFile();
        SourceFileSnapshot before = validator.Capture(path);

        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        SourceFileSnapshot after = validator.Capture(path);

        SourceIntegrityValidationResult result = validator.Compare(before, after);

        Assert.Equal(SourceIntegrityStatus.Warning, result.Status);

        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    [Fact]
    public void Validator_DoesNotModifyFileLength()
    {
        string path = CreateFile("unchanged content");
        long originalLength = new FileInfo(path).Length;

        validator.Capture(path);

        Assert.Equal(originalLength, new FileInfo(path).Length);
    }

    [Fact]
    public void Validator_DoesNotModifyLastWriteTimeUtc()
    {
        string path = CreateFile();
        DateTime originalWrite = File.GetLastWriteTimeUtc(path);

        validator.Capture(path);

        Assert.Equal(originalWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Validator_DoesNotAlterReadOnlyAttribute()
    {
        string path = CreateFile();
        FileAttributes originalAttributes = File.GetAttributes(path);

        validator.Capture(path);

        Assert.Equal(originalAttributes, File.GetAttributes(path));
    }

    [Fact]
    public void Paths_AreNormalizedConsistently()
    {
        string path = CreateFile();
        string relativeStyle = Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path));

        SourceFileSnapshot fromDirect = validator.Capture(path);
        SourceFileSnapshot fromRelative = validator.Capture(relativeStyle);

        Assert.Equal(fromDirect.Path, fromRelative.Path);
    }
}
