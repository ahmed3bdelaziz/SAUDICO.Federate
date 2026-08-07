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

            string runId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            string exportName = Path.GetFileNameWithoutExtension(job.Source) + ".__SAUDICO_TEMP__." + runId;
            string temporary = Path.Combine(job.OutputFolder, exportName + ".nwc");

            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            bool navisworksAvailable = OptionalFunctionalityUtils.IsNavisworksExporterAvailable();
            DateTime exportBeforeUtc = DateTime.UtcNow;

            // A/B diagnostic switch (SAUDICO.Federate.Shared.DiagnosticSettings.ExportScopeMode):
            // View reproduces production behavior; Model is a temporary diagnostic-only probe.
            // Never both in the same export — one controlled build per test.
            NavisworksExportScope diagnosticScope = DiagnosticSettings.ExportScopeMode == DiagnosticScopeMode.Model
                ? NavisworksExportScope.Model
                : NavisworksExportScope.View;
            ElementId diagnosticViewId = diagnosticScope == NavisworksExportScope.Model
                ? ElementId.InvalidElementId
                : view.Id;

            // These diagnostics are logged unconditionally, not gated by job.Settings.Log —
            // same as the unhandled-exception and source-integrity-violation logging above —
            // because the failure this is diagnosing occurred on a run with Export Log off,
            // leaving nothing in the log to inspect afterward.
            Log.Information(
                "NwcExportBefore {NwcExportBefore} ExportFolder {ExportFolder} ExportName {ExportName} " +
                "ExpectedNwcPath {ExpectedNwcPath} NavisworksExporterAvailable {NavisworksExporterAvailable} " +
                "ExportScope {ExportScope} ViewId {ViewId} ViewValid {ViewValid} ExportLinks {ExportLinks} " +
                "DivideFileIntoLevels {DivideFileIntoLevels}",
                exportBeforeUtc, job.OutputFolder, exportName, temporary, navisworksAvailable,
                diagnosticScope, diagnosticViewId, view.IsValidObject, job.Settings.ExportLinks,
                job.Settings.DivideLevels);

            if (!navisworksAvailable)
            {
                const string message = "Navisworks exporter is not available in this Revit session.";
                Log.Error("NwcExportSkipped Reason={Reason}", message);
                return Result.Fail(message);
            }

            LogPreExportDiagnostics(document, view, diagnosticScope, diagnosticViewId, job.Settings.ExportLinks);

            DialogWatch.Reset();
            bool exportReturned;

            try
            {
                using (NavisworksExportOptions options = CreateOptions(diagnosticScope, diagnosticViewId, job.Settings))
                {
                    document.Export(job.OutputFolder, exportName, options);
                }

                exportReturned = true;
            }
            // Order matters: Revit's own exception types (Autodesk.Revit.Exceptions.*) have
            // a different hierarchy than System's same-named types —
            // OptionalFunctionalityNotAvailableException derives from InvalidOperationException,
            // and InvalidPathArgumentException derives from ArgumentException — so the more
            // specific catches must come first.
            catch (Autodesk.Revit.Exceptions.OptionalFunctionalityNotAvailableException ex)
            {
                Log.Error(ex, "NwcExportThrew ExceptionType=OptionalFunctionalityNotAvailableException");
                return Result.Fail("Navisworks exporter is not available: " + ex.Message);
            }
            catch (Autodesk.Revit.Exceptions.InvalidPathArgumentException ex)
            {
                Log.Error(
                    ex,
                    "NwcExportThrew ExceptionType=InvalidPathArgumentException ExportFolder={ExportFolder} ExportName={ExportName}",
                    job.OutputFolder, exportName);
                return Result.Fail("Invalid export path: " + ex.Message);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                Log.Error(ex, "NwcExportThrew ExceptionType=ArgumentException");
                return Result.Fail("Invalid export argument: " + ex.Message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException ex)
            {
                Log.Error(ex, "NwcExportThrew ExceptionType=OperationCanceledException");
                return Result.Fail("Export was cancelled: " + ex.Message);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                Log.Error(ex, "NwcExportThrew ExceptionType=InvalidOperationException");
                return Result.Fail("Export failed: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NwcExportThrew ExceptionType={ExceptionType}", ex.GetType().FullName);
                return Result.Fail("Export failed: " + ex.Message);
            }

            bool expectedExists = File.Exists(temporary);
            long expectedLength = expectedExists ? new FileInfo(temporary).Length : 0;
            DateTime? expectedLastWriteUtc = expectedExists ? File.GetLastWriteTimeUtc(temporary) : (DateTime?)null;

            // Only NWC files in the output folder written during this run are considered —
            // pre-existing NWCs from earlier jobs/runs must not be mistaken for this export's output.
            string[] newlyCreated;
            try
            {
                newlyCreated = Directory.GetFiles(job.OutputFolder, "*.nwc")
                    .Where(f => File.GetLastWriteTimeUtc(f) >= exportBeforeUtc)
                    .ToArray();
            }
            catch (Exception)
            {
                newlyCreated = Array.Empty<string>();
            }

            Log.Information(
                "NwcExportReturned {NwcExportReturned} ExpectedFileExists {ExpectedFileExists} " +
                "ExpectedFileLength {ExpectedFileLength} ExpectedFileLastWriteUtc {ExpectedFileLastWriteUtc} " +
                "NewlyCreatedNwcFiles {@NewlyCreatedNwcFiles}",
                exportReturned, expectedExists, expectedLength, expectedLastWriteUtc, newlyCreated);

            if (DialogWatch.Matched)
            {
                Log.Error(
                    "NwcExportFailedDialog DialogId={DialogId} Message={Message}",
                    DialogWatch.DialogId, DialogWatch.Message);

                return Result.Fail("Navisworks rejected the selected export scope because no suitable geometry was found.");
            }

            if (!expectedExists || expectedLength == 0)
            {
                Log.Error(
                    "NwcExportFailed ExpectedNwcPath={ExpectedNwcPath} ExpectedFileExists={ExpectedFileExists} " +
                    "ExpectedFileLength={ExpectedFileLength} NewlyCreatedNwcFiles={@NewlyCreatedNwcFiles}",
                    temporary, expectedExists, expectedLength, newlyCreated);

                return Result.Fail("NWC missing or empty after export.");
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temporary, destination);
            return Result.Success(destination);
        }

        /// <summary>
        /// Diagnostic-only, read-only element-count snapshot logged immediately before
        /// Document.Export. Never modifies the document. Answers the A/B question: does the
        /// temporary federation view have exportable geometry that the document itself has,
        /// or is the opened ACC document itself missing native Navisworks-exportable geometry?
        /// </summary>
        private static void LogPreExportDiagnostics(
            Document document, View3D view, NavisworksExportScope scope, ElementId exportViewId, bool exportLinks)
        {
            int totalFamilyInstances = new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().GetElementCount();
            int viewFamilyInstances = new FilteredElementCollector(document, view.Id).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().GetElementCount();

            int totalWalls = new FilteredElementCollector(document).OfClass(typeof(Wall)).WhereElementIsNotElementType().GetElementCount();
            int viewWalls = new FilteredElementCollector(document, view.Id).OfClass(typeof(Wall)).WhereElementIsNotElementType().GetElementCount();

            int totalFloors = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType().GetElementCount();
            int viewFloors = new FilteredElementCollector(document, view.Id).OfClass(typeof(Floor)).WhereElementIsNotElementType().GetElementCount();

            int totalRoofs = new FilteredElementCollector(document).OfClass(typeof(RoofBase)).WhereElementIsNotElementType().GetElementCount();
            int viewRoofs = new FilteredElementCollector(document, view.Id).OfClass(typeof(RoofBase)).WhereElementIsNotElementType().GetElementCount();

            int totalCeilings = new FilteredElementCollector(document).OfClass(typeof(Ceiling)).WhereElementIsNotElementType().GetElementCount();
            int viewCeilings = new FilteredElementCollector(document, view.Id).OfClass(typeof(Ceiling)).WhereElementIsNotElementType().GetElementCount();

            int totalDirectShapes = new FilteredElementCollector(document).OfClass(typeof(DirectShape)).WhereElementIsNotElementType().GetElementCount();
            int viewDirectShapes = new FilteredElementCollector(document, view.Id).OfClass(typeof(DirectShape)).WhereElementIsNotElementType().GetElementCount();

            var linkInstances = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<RevitLinkInstance>()
                .ToList();
            int linkInstanceCount = linkInstances.Count;
            int loadedLinkInstanceCount = linkInstances.Count(x => x.GetLinkDocument() != null);

            Log.Information(
                "NwcExportDiagnostics IsModelInCloud={IsModelInCloud} IsWorkshared={IsWorkshared} " +
                "ViewValid={ViewValid} ViewIsTemplate={ViewIsTemplate} ViewType={ViewType} ViewId={ViewId} " +
                "ExportScope={ExportScope} ExportViewId={ExportViewId} ExportLinks={ExportLinks} " +
                "TotalFamilyInstances={TotalFamilyInstances} ViewFamilyInstances={ViewFamilyInstances} " +
                "TotalWalls={TotalWalls} ViewWalls={ViewWalls} TotalFloors={TotalFloors} ViewFloors={ViewFloors} " +
                "TotalRoofs={TotalRoofs} ViewRoofs={ViewRoofs} TotalCeilings={TotalCeilings} ViewCeilings={ViewCeilings} " +
                "TotalDirectShapes={TotalDirectShapes} ViewDirectShapes={ViewDirectShapes} " +
                "RevitLinkInstanceCount={RevitLinkInstanceCount} LoadedRevitLinkInstanceCount={LoadedRevitLinkInstanceCount}",
                document.IsModelInCloud, document.IsWorkshared,
                view.IsValidObject, view.IsTemplate, view.ViewType, view.Id,
                scope, exportViewId, exportLinks,
                totalFamilyInstances, viewFamilyInstances,
                totalWalls, viewWalls, totalFloors, viewFloors,
                totalRoofs, viewRoofs, totalCeilings, viewCeilings,
                totalDirectShapes, viewDirectShapes,
                linkInstanceCount, loadedLinkInstanceCount);
        }

        private static NavisworksExportOptions CreateOptions(NavisworksExportScope scope, ElementId viewId, ExportSettings settings)
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
                ExportScope = scope,
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

