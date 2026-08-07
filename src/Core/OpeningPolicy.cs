using System;
using System.IO;

namespace SAUDICO.Federate.Core
{
    /// <summary>
    /// Pure domain policy for determining how to open a source document.
    /// Contains NO Revit API references.
    /// Maps to Revit DetachFromCentralOption in the adapter layer.
    /// </summary>
    public static class SourceOpeningPolicy
    {
        /// <summary>
        /// Determines the opening strategy based on source kind.
        /// Returns pure domain data only.
        /// </summary>
        public static SourceOpeningResult GetOpenPlan(SourceKind kind, string? localPath = null, AccCloudDescriptor? cloudDescriptor = null)
        {
            return kind switch
            {
                SourceKind.LocalFile => CreateLocalPlan(localPath),
                SourceKind.FileCentral => CreateCentralPlan(localPath),
                SourceKind.AccCloudModel => CreateCloudPlan(cloudDescriptor),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), $"Unknown source kind: {kind}")
            };
        }

        private static SourceOpeningResult CreateLocalPlan(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Local file path required for LocalFile source.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"Local source file not found: {path}", path);

            return new SourceOpeningResult
            {
                Kind = SourceKind.LocalFile,
                LocalFilePath = path,
                DetachPolicy = "None",
                WorksetPolicy = "PreserveOpenedVisibility"
            };
        }

        private static SourceOpeningResult CreateCentralPlan(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Central file path required for FileCentral source.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"Central file not found: {path}", path);

            return new SourceOpeningResult
            {
                Kind = SourceKind.FileCentral,
                LocalFilePath = path,
                DetachPolicy = "DetachAndPreserveWorksets",
                WorksetPolicy = "OpenAllWorksets"
            };
        }

        private static SourceOpeningResult CreateCloudPlan(AccCloudDescriptor? descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor), "Cloud descriptor required for AccCloudModel source.");

            // CRITICAL: Validate Revit identity BEFORE attempting to open
            descriptor.Validate();

            return new SourceOpeningResult
            {
                Kind = SourceKind.AccCloudModel,
                CloudDescriptor = descriptor,
                DetachPolicy = "DoNotDetach",
                WorksetPolicy = "OpenAllWorksets"
            };
        }
    }
}
