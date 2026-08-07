using System;using Autodesk.Revit.ApplicationServices;using Autodesk.Revit.DB;using SAUDICO.Federate.Shared;namespace SAUDICO.Federate.Core;public static class Detector{public static ModelKind Get(string p){using var i=BasicFileInfo.Extract(p);return !i.IsWorkshared?ModelKind.Local:i.IsCentral?ModelKind.Central:ModelKind.WorksharedLocal;}}public static class Opener{public static Document Open(Job j,Application a){var o=new OpenOptions();if(j.Kind==ModelKind.Central)o.DetachFromCentralOption=DetachFromCentralOption.DetachAndPreserveWorksets;if(j.Kind!=ModelKind.Local)o.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));return a.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(j.Source),o);}

    /// <summary>The exact, unconditional detach policy every ACC open uses — never anything else. Exposed as a constant so it can be asserted by a unit test without constructing any Revit API object.</summary>
    public const DetachFromCentralOption AccDetachOption = DetachFromCentralOption.DoNotDetach;

    /// <summary>The exact, unconditional workset-open policy every ACC open uses — never anything else. Exposed as a constant so it can be asserted by a unit test without constructing any Revit API object.</summary>
    public const WorksetConfigurationOption AccWorksetOption = WorksetConfigurationOption.OpenAllWorksets;

    /// <summary>
    /// Opens a verified ACC/Revit Cloud Model in memory: DoNotDetach (never
    /// detach a cloud model), OpenAllWorksets, and the official
    /// DefaultOpenFromCloudCallback (discards any local cache conflict and
    /// opens the latest cloud version — safe here because this path never
    /// writes back, so there is never a real local change to lose). Never
    /// calls OpenAndActivateDocument. Validates the opened document is a
    /// genuine cloud model whose ProjectGuid/ModelGuid match the ones this
    /// job was verified against before returning it.
    /// </summary>
    /// <summary>
    /// Opens the downloaded local temporary copy of a plain uploaded ACC
    /// file. The copy is treated exactly like any other file-based source:
    /// its own kind is detected from the file itself, so a downloaded copy
    /// of a workshared central model still opens with
    /// DetachAndPreserveWorksets and never attaches to any central. The
    /// ACC-hosted original is never opened.
    /// </summary>
    public static Document OpenAccLocalCopy(Job job, Application application)
    {
        string path = job.AccLocalCopyPath
            ?? throw new InvalidOperationException("This ACC file has not been downloaded and cannot be opened.");

        if (!System.IO.File.Exists(path))
        {
            throw new InvalidOperationException("The downloaded ACC file is no longer available and cannot be opened.");
        }

        ModelKind detected = Detector.Get(path);

        OpenOptions options = new OpenOptions();
        if (detected == ModelKind.Central)
        {
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
        }

        if (detected != ModelKind.Local)
        {
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        }

        return application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), options);
    }

    public static Document OpenAcc(Job job, Application application)
    {
        if (!AccOpenPlanner.TryBuildPlan(job, out AccOpenPlan? plan, out string? blockReason))
        {
            throw new InvalidOperationException(blockReason);
        }

        string region = AccRegionMapper.ToRevitRegionConstant(plan!.Region);
        ModelPath cloudPath = ModelPathUtils.ConvertCloudGUIDsToCloudPath(region, plan.ProjectGuid, plan.ModelGuid);

        OpenOptions options = new OpenOptions { DetachFromCentralOption = AccDetachOption };
        options.SetOpenWorksetsConfiguration(new WorksetConfiguration(AccWorksetOption));

        Document document = application.OpenDocumentFile(cloudPath, options, new DefaultOpenFromCloudCallback());

        if (document == null || !document.IsValidObject)
        {
            throw new InvalidOperationException("ACC cloud model failed to open.");
        }

        if (!document.IsModelInCloud)
        {
            document.Close(false);
            throw new InvalidOperationException("The opened document is not a Revit cloud model.");
        }

        ModelPath openedPath = document.GetCloudModelPath();
        if (openedPath.GetProjectGUID() != plan.ProjectGuid || openedPath.GetModelGUID() != plan.ModelGuid)
        {
            document.Close(false);
            throw new InvalidOperationException("The opened cloud model's identifiers do not match the verified ProjectGuid/ModelGuid.");
        }

        return document;
    }
}
