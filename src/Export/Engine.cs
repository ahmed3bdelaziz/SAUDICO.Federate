using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Serilog;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.Export
{
    public static class Engine
    {
        public static Result Run(Job job, Application application, Action<string> log)
        {
            return Run(job, application, log, new SourceIntegrityValidator());
        }

        public static Result Run(Job job, Application application, Action<string> log, ISourceIntegrityValidator integrityValidator)
        {
            Document document = null!;
            job.Start = DateTime.Now;
            Result result;
            bool isAcc = job.Kind == ModelKind.Acc;

            // Step 1: capture the source snapshot before the document is opened.
            // ACC cloud sources have no local file to snapshot — Job.Source is a
            // display name, not a path — so file-metadata integrity checking is
            // skipped entirely rather than inventing size/timestamp/read-only
            // values for it (see Finalize below for the NotApplicableCloudSource
            // status this produces instead).
            SourceFileSnapshot? before = null;
            if (!isAcc)
            {
                before = integrityValidator.Capture(job.Source);
                job.IntegrityBefore = before;
            }

            if (job.Settings.Log)
            {
                Log.Information(
                    "Job started {Source} {SourceType} {OpeningPolicy} {Start} {Output} {Coordinates} {DetailLevel} {Parameters} {ExportLinks} {DivideLevels} {Faceting}",
                    job.Source, job.Kind, OpeningPolicy(job.Kind), job.Start, job.OutputFolder,
                    job.Settings.Coordinates, job.Settings.Detail, job.Settings.Parameters,
                    job.Settings.ExportLinks, job.Settings.DivideLevels, job.Settings.Faceting);
            }

            try
            {
                // Step 2: open the document using the safety-verified opening policy.
                job.State = State.Opening;
                log("Opening model");
                document = isAcc
                    ? (job.AccLocalCopyPath != null
                        ? Opener.OpenAccLocalCopy(job, application)
                        : Opener.OpenAcc(job, application))
                    : Opener.Open(job, application);

                // Step 3: prepare the temporary federation view.
                job.State = State.Preparing;
                log("Creating temporary federation view");
                if (job.Settings.Log)
                {
                    Log.Information("Temporary-view creation started for {Source}", job.Source);
                }

                View3D view = ViewBuilder.Create(document, job.Settings);

                // Step 4: export the NWC.
                job.State = State.Exporting;
                log("Exporting NWC");
                if (job.Settings.Log)
                {
                    Log.Information("Export started for {Source}", job.Source);
                }

                result = Nwc.Export(document, view, job);
            }
            catch (Exception ex)
            {
                result = Result.Fail(ex.Message);

                // Fatal/unhandled errors are always logged, regardless of the Export Log setting.
                Log.Error(ex, "Unhandled exception while processing job for {Source}", job.Source);
            }
            finally
            {
                // Step 5: the document is always closed without saving, from this finally block.
                if (document != null && document.IsValidObject)
                {
                    job.State = State.Closing;
                    document.Close(false);
                }

                job.End = DateTime.Now;
            }

            // Step 6/7: capture the post-processing snapshot and compare integrity.
            // This only runs after Close(false) above. Not applicable for ACC
            // cloud sources — reported honestly instead of fabricated.
            SourceIntegrityValidationResult integrity;
            if (isAcc)
            {
                integrity = new SourceIntegrityValidationResult
                {
                    Status = SourceIntegrityStatus.NotApplicableCloudSource,
                    Message = "Source integrity checks are not applicable to ACC cloud-hosted sources.",
                    Before = null,
                    After = null,
                };
            }
            else
            {
                SourceFileSnapshot after = integrityValidator.Capture(job.Source);
                job.IntegrityAfter = after;
                integrity = integrityValidator.Compare(before!, after);
            }

            job.IntegrityStatus = integrity.Status;
            job.IntegrityMessage = integrity.Message;

            // Step 8: finalize job status/result and report.
            return Finalize(job, result, integrity, log);
        }

        private static Result Finalize(Job job, Result exportResult, SourceIntegrityValidationResult integrity, Action<string> log)
        {
            if (integrity.Status == SourceIntegrityStatus.Fail)
            {
                job.State = State.Failed;
                job.Error = integrity.Message;

                if (!exportResult.Ok)
                {
                    job.Warnings = Append(job.Warnings, "Original export error: " + exportResult.Error);
                }

                log(integrity.Message);

                // Source-integrity violations are always logged, regardless of the Export Log setting.
                Log.Error(
                    "Source integrity violation for {Source}. Before: {@Before} After: {@After}",
                    job.Source, integrity.Before, integrity.After);

                return Result.Fail(integrity.Message);
            }

            if (integrity.Status == SourceIntegrityStatus.Warning)
            {
                job.Warnings = Append(job.Warnings, integrity.Message);

                if (job.Settings.Log)
                {
                    Log.Warning(
                        "Source integrity warning for {Source}: {Message}. Before: {@Before} After: {@After}",
                        job.Source, integrity.Message, integrity.Before, integrity.After);
                }
            }

            if (!exportResult.Ok && !exportResult.Skipped)
            {
                job.State = State.Failed;
                job.Error = exportResult.Error;
            }
            else if (exportResult.Skipped)
            {
                job.State = State.Skipped;
            }
            else
            {
                job.State = State.Succeeded;
            }

            if (job.Settings.Log)
            {
                Log.Information(
                    "Job completed {Source} {Status} {Duration} {Warnings} {Errors} {IntegrityStatus}",
                    job.Source, job.State, (job.End - job.Start).TotalSeconds, job.Warnings, job.Error, integrity.Status);
            }

            return exportResult;
        }

        private static string? Append(string? existing, string addition)
        {
            return string.IsNullOrEmpty(existing) ? addition : existing + " | " + addition;
        }

        private static string OpeningPolicy(ModelKind kind)
        {
            return kind switch
            {
                ModelKind.Central => "DetachAndPreserveWorksets",
                ModelKind.WorksharedLocal => "OpenAllWorksets",
                ModelKind.Acc => "DoNotDetach",
                _ => "Normal"
            };
        }
    }

    public static class ViewBuilder
    {
        public static View3D Create(Document document, ExportSettings settings)
        {
            using (Transaction transaction = new Transaction(document, "SAUDICO temporary federation view"))
            {
                try
                {
                    transaction.Start();

                    ElementId typeId = new FilteredElementCollector(document)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .First(x => x.ViewFamily == ViewFamily.ThreeDimensional)
                        .Id;

                    View3D view = View3D.CreateIsometric(document, typeId);
                    view.Name = C.Temp + "-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();

                    if (view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        document.Regenerate();
                    }

                    if (!view.HasDetailLevel() || !view.CanModifyDetailLevel())
                    {
                        throw new InvalidOperationException("Temporary view Detail Level cannot be modified.");
                    }

                    view.DetailLevel = settings.Detail == DetailMode.Coarse
                        ? ViewDetailLevel.Coarse
                        : settings.Detail == DetailMode.Medium
                            ? ViewDetailLevel.Medium
                            : ViewDetailLevel.Fine;

                    view.Discipline = ViewDiscipline.Coordination;
                    view.IsSectionBoxActive = false;
                    view.CropBoxActive = false;
                    view.CropBoxVisible = false;

                    foreach (ElementId filterId in view.GetFilters().ToList())
                    {
                        view.RemoveFilter(filterId);
                    }

                    foreach (Category category in document.Settings.Categories)
                    {
                        if (!category.get_AllowsVisibilityControl(view))
                        {
                            continue;
                        }

                        bool hide =
                            (settings.ExAnnotations && category.CategoryType == CategoryType.Annotation) ||
                            IsExcludedCategory(category, settings);

                        try
                        {
                            view.SetCategoryHidden(category.Id, hide);
                            view.SetCategoryOverrides(category.Id, new OverrideGraphicSettings());
                        }
                        catch
                        {
                            // Some system categories cannot be controlled in this view.
                        }
                    }

                    if (settings.ExModelLines || settings.ExRoom || settings.ExArea || settings.ExSpace)
                    {
                        var curveIds = new FilteredElementCollector(document, view.Id)
                            .OfClass(typeof(CurveElement))
                            .WhereElementIsNotElementType()
                            .Cast<CurveElement>()
                            .Where(x => x.Category != null &&
                                (settings.ExModelLines || IsExcludedCategory(x.Category, settings)))
                            .Where(x => x.CanBeHidden(view))
                            .Select(x => x.Id)
                            .ToList();

                        if (curveIds.Count > 0)
                        {
                            view.HideElements(curveIds);
                        }
                    }

                    transaction.Commit();
                    return view;
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }

                    throw;
                }
            }
        }

        private static bool IsExcludedCategory(Category category, ExportSettings settings)
        {
            long categoryId = GetElementIdValue(category.Id);

            return
                (settings.ExRoom && categoryId == (long)BuiltInCategory.OST_RoomSeparationLines) ||
                (settings.ExArea && categoryId == (long)BuiltInCategory.OST_AreaSchemeLines) ||
                (settings.ExModelLines && categoryId == (long)BuiltInCategory.OST_Lines);
        }

        private static long GetElementIdValue(ElementId id)
        {
            return id.Value;
        }
    }

    public static class Nwc
    {
        public static Result Export(Document document, View3D view, Job job)
        {
            Directory.CreateDirectory(job.OutputFolder);

            string destination = Path.Combine(
                job.OutputFolder,
                Path.GetFileNameWithoutExtension(job.Source) + ".nwc");

            job.Output = destination;

            if (File.Exists(destination) && !job.Settings.Overwrite)
            {
                return Result.Skip(destination);
            }

            string temporary = Path.Combine(
                job.OutputFolder,
                Path.GetFileNameWithoutExtension(job.Source) + ".__SAUDICO_TEMP__.nwc");

            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            using (NavisworksExportOptions options = CreateOptions(view.Id, job.Settings))
            {
                document.Export(
                    job.OutputFolder,
                    Path.GetFileNameWithoutExtension(temporary),
                    options);
            }

            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
            {
                return Result.Fail("NWC missing or empty after export.");
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temporary, destination);
            return Result.Success(destination);
        }

        private static NavisworksExportOptions CreateOptions(ElementId viewId, ExportSettings settings)
        {
            NavisworksParameters parameters = NavisworksParameters.None;
            if (settings.Properties)
            {
                parameters = settings.Parameters == ParamMode.All
                    ? NavisworksParameters.All
                    : settings.Parameters == ParamMode.Elements
                        ? NavisworksParameters.Elements
                        : NavisworksParameters.None;
            }

            return new NavisworksExportOptions
            {
                ExportScope = NavisworksExportScope.View,
                ViewId = viewId,
                ExportLinks = settings.ExportLinks,
                ExportElementIds = settings.ElementIds,
                ConvertElementProperties = settings.Properties,
                Parameters = parameters,
                Coordinates = settings.Coordinates == CoordMode.Shared
                    ? NavisworksCoordinates.Shared
                    : NavisworksCoordinates.Internal,
                FindMissingMaterials = settings.FindMaterials,
                DivideFileIntoLevels = settings.DivideLevels,
                ExportParts = settings.Parts,
                ConvertLinkedCADFormats = settings.LinkedCad,
                ExportRoomAsAttribute = settings.RoomAttributes,
                ExportRoomGeometry = settings.RoomGeometry,
                ConvertLights = settings.Lights,
                ExportUrls = settings.Urls,
                FacetingFactor = settings.Faceting
            };
        }
    }
}

