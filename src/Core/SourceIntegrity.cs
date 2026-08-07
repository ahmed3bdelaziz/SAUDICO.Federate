using System;
using System.IO;

namespace SAUDICO.Federate.Core;

public sealed class SourceFileSnapshot
{
    public string Path { get; set; } = "";
    public bool Exists { get; set; }
    public long Length { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public bool IsReadOnly { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}

public enum SourceIntegrityStatus { Pass, Warning, Fail, NotApplicableCloudSource }

public sealed class SourceIntegrityValidationResult
{
    public SourceIntegrityStatus Status { get; set; }
    public string Message { get; set; } = "";
    public SourceFileSnapshot? Before { get; set; }
    public SourceFileSnapshot? After { get; set; }
}

public interface ISourceIntegrityValidator
{
    SourceFileSnapshot Capture(string path);
    SourceIntegrityValidationResult Compare(SourceFileSnapshot before, SourceFileSnapshot after);
}

/// <summary>
/// Read-only file-metadata integrity check for file-based RVT sources.
/// Never opens the file stream, never touches Collaboration Cache, never
/// writes to the source. Not applicable to a future ACC cloud-model descriptor.
/// </summary>
public sealed class SourceIntegrityValidator : ISourceIntegrityValidator
{
    public SourceFileSnapshot Capture(string path)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch
        {
            normalized = path;
        }

        try
        {
            FileInfo info = new FileInfo(normalized);
            bool exists = info.Exists;

            return new SourceFileSnapshot
            {
                Path = normalized,
                Exists = exists,
                Length = exists ? info.Length : 0,
                LastWriteTimeUtc = exists ? info.LastWriteTimeUtc : DateTime.MinValue,
                IsReadOnly = exists && info.IsReadOnly,
                CapturedAtUtc = DateTime.UtcNow
            };
        }
        catch
        {
            // Inaccessible during capture is treated the same as missing:
            // the comparison step turns this into a Fail result.
            return new SourceFileSnapshot
            {
                Path = normalized,
                Exists = false,
                Length = 0,
                LastWriteTimeUtc = DateTime.MinValue,
                IsReadOnly = false,
                CapturedAtUtc = DateTime.UtcNow
            };
        }
    }

    public SourceIntegrityValidationResult Compare(SourceFileSnapshot before, SourceFileSnapshot after)
    {
        const string violation = "Source Integrity Violation: the source RVT changed during processing.";

        if (!before.Exists)
        {
            return new SourceIntegrityValidationResult
            {
                Status = SourceIntegrityStatus.Fail,
                Message = violation,
                Before = before,
                After = after
            };
        }

        if (!after.Exists)
        {
            return new SourceIntegrityValidationResult
            {
                Status = SourceIntegrityStatus.Fail,
                Message = violation,
                Before = before,
                After = after
            };
        }

        if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
            return new SourceIntegrityValidationResult
            {
                Status = SourceIntegrityStatus.Fail,
                Message = violation,
                Before = before,
                After = after
            };
        }

        if (before.IsReadOnly != after.IsReadOnly)
        {
            return new SourceIntegrityValidationResult
            {
                Status = SourceIntegrityStatus.Warning,
                Message = "Source read-only attribute changed during processing; size and timestamp are unchanged.",
                Before = before,
                After = after
            };
        }

        return new SourceIntegrityValidationResult
        {
            Status = SourceIntegrityStatus.Pass,
            Message = "Source integrity verified.",
            Before = before,
            After = after
        };
    }
}
