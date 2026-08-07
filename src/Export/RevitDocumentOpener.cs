using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using SAUDICO.Federate.Core;

namespace SAUDICO.Federate.Export;

/// <summary>Failure raised when Revit opens a cloud model other than the verified requested identity.</summary>
public sealed class CloudModelIdentityMismatchException : Exception
{
    public CloudModelIdentityMismatchException(RevitCloudIdentity requested, Guid actualProjectGuid, Guid actualModelGuid, Exception? closeError)
        : base($"Opened cloud model identity mismatch. Expected project {requested.RevitProjectGuid} / model {requested.RevitModelGuid}; actual project {actualProjectGuid} / model {actualModelGuid}.")
    {
        Requested = requested;
        ActualProjectGuid = actualProjectGuid;
        ActualModelGuid = actualModelGuid;
        CloseError = closeError;
    }

    public RevitCloudIdentity Requested { get; }
    public Guid ActualProjectGuid { get; }
    public Guid ActualModelGuid { get; }
    public Exception? CloseError { get; }
}

public sealed class RevitDocumentOpenException : Exception
{
    public RevitDocumentOpenException(string code, string message, Exception innerException) : base(message, innerException) => Code = code;
    public string Code { get; }
}

public interface IRevitDocumentOpener
{
    Document OpenAccCloud(Application app, AccCloudDescriptor descriptor, OpenPlan plan);
}

/// <summary>
/// Revit-facing document opener. It must be invoked only from a valid Revit API context,
/// such as IExternalEventHandler.Execute; it never transfers Revit objects to background code.
/// </summary>
public sealed class RevitDocumentOpener : IRevitDocumentOpener
{
    public static Document Open(Job job, Application app)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        if (app == null) throw new ArgumentNullException(nameof(app));

        return job.SourceKind switch
        {
            SourceKind.LocalFile => OpenLocal(app, job.LocalFilePath, OpenPlan.ForLocalFile()),
            SourceKind.FileCentral => OpenFileCentral(app, job.LocalFilePath, OpenPlan.ForFileCentral()),
            SourceKind.AccCloudModel => new RevitDocumentOpener().OpenAccCloud(app, job.CloudDescriptor ?? throw new ArgumentException("Cloud descriptor is required.", nameof(job)), OpenPlan.ForAccCloud()),
            _ => throw new ArgumentOutOfRangeException(nameof(job.SourceKind))
        };
    }

    public Document OpenAccCloud(Application app, AccCloudDescriptor descriptor, OpenPlan plan)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        ValidatePlan(plan, SourceKind.AccCloudModel);
        descriptor.Validate();

        ModelPath path = ModelPathUtils.ConvertCloudGUIDsToCloudPath(
            descriptor.RevitIdentity.Region,
            descriptor.RevitIdentity.RevitProjectGuid,
            descriptor.RevitIdentity.RevitModelGuid);

        return OpenAndVerify(app, path, CreateOptions(plan), descriptor.RevitIdentity, plan.VerifyIdentityAfterOpen);
    }

    public static Document OpenFileCentral(Application app, string? path, OpenPlan plan)
    {
        ValidatePlan(plan, SourceKind.FileCentral);
        return OpenFile(app, path, plan);
    }

    public static Document OpenLocal(Application app, string? path, OpenPlan plan)
    {
        ValidatePlan(plan, SourceKind.LocalFile);
        return OpenFile(app, path, plan);
    }

    private static Document OpenFile(Application app, string? path, OpenPlan plan)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new RevitDocumentOpenException("OPEN_LOCAL_FILE_NOT_FOUND", "The requested source file does not exist.", new FileNotFoundException("Source file not found.", path));

        ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        return OpenAndVerify(app, modelPath, CreateOptions(plan), null, false);
    }

    private static OpenOptions CreateOptions(OpenPlan plan)
    {
        OpenOptions options = new OpenOptions();
        options.DetachFromCentralOption = plan.DetachStrategy switch
        {
            DetachStrategy.DetachAndPreserveWorksets => DetachFromCentralOption.DetachAndPreserveWorksets,
            DetachStrategy.DoNotDetach => DetachFromCentralOption.DoNotDetach,
            _ => DetachFromCentralOption.DoNotDetach
        };

        if (plan.WorksetStrategy == WorksetStrategy.OpenAll)
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        return options;
    }

    private static Document OpenAndVerify(Application app, ModelPath path, OpenOptions options, RevitCloudIdentity? requestedIdentity, bool verifyIdentity)
    {
        Document? document = null;
        try
        {
            document = app.OpenDocumentFile(path, options);
            if (document == null || !document.IsValidObject)
                throw new InvalidOperationException("Revit did not return a valid opened document.");

            if (verifyIdentity && requestedIdentity != null)
                VerifyCloudIdentity(document, requestedIdentity);

            return document;
        }
        catch (CloudModelIdentityMismatchException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Exception? closeError = CloseWithoutSaving(document);
            string message = closeError == null ? ex.Message : ex.Message + " Cleanup failed: " + closeError.Message;
            throw new RevitDocumentOpenException(MapFailureCode(ex), message, ex);
        }
    }

    private static void VerifyCloudIdentity(Document document, RevitCloudIdentity requested)
    {
        if (!document.IsModelInCloud)
            throw new InvalidOperationException("The opened document is not a Revit cloud model.");

        ModelPath actualPath = document.GetCloudModelPath();
        Guid actualProjectGuid = actualPath.GetProjectGUID();
        Guid actualModelGuid = actualPath.GetModelGUID();
        if (actualProjectGuid == requested.RevitProjectGuid && actualModelGuid == requested.RevitModelGuid)
            return;

        Exception? closeError = CloseWithoutSaving(document);
        throw new CloudModelIdentityMismatchException(requested, actualProjectGuid, actualModelGuid, closeError);
    }

    private static Exception? CloseWithoutSaving(Document? document)
    {
        if (document == null || !document.IsValidObject) return null;
        try { document.Close(false); return null; }
        catch (Exception ex) { return ex; }
    }

    private static void ValidatePlan(OpenPlan plan, SourceKind expectedKind)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (plan.SourceKind != expectedKind) throw new ArgumentException("Open plan does not match the source kind.", nameof(plan));
    }

    private static string MapFailureCode(Exception exception)
    {
        string name = exception.GetType().Name;
        if (exception is OperationCanceledException) return "OPEN_CANCELLED";
        if (name.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0) return "REVIT_CLOUD_UNAUTHORIZED";
        if (name.IndexOf("SignIn", StringComparison.OrdinalIgnoreCase) >= 0) return "REVIT_SIGNIN_REQUIRED";
        if (name.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0) return "REVIT_RELEASE_MISMATCH";
        if (name.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0) return "CLOUD_NETWORK_FAILURE";
        if (name.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0) return "CLOUD_SERVICE_UNAVAILABLE";
        if (name.IndexOf("Central", StringComparison.OrdinalIgnoreCase) >= 0) return "OPEN_CENTRAL_CONTENTION";
        return "OPEN_UNKNOWN_FAILURE";
    }
}
