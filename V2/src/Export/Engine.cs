using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.Export
{
    public static class Engine
    {
        public static Result Run(Job job, Application application, Action<string> log)
        {
            Document document = null!;
            job.Start = DateTime.Now;

            try
            {
                job.State = State.Opening;
                log("Opening model");
                document = Opener.Open(job, application);

                job.State = State.Preparing;
                log("Creating temporary federation view");
                View3D view = ViewBuilder.Create(document, job.Settings);

                job.State = State.Exporting;
                log("Exporting NWC");
                return Nwc.Export(document, view, job);
            }
            catch (Exception ex)
            {
                return Result.Fail(ex.Message);
            }
            finally
            {
                if (document != null && document.IsValidObject)
                {
                    job.State = State.Closing;
                    document.Close(false);
                }

                job.End = DateTime.Now;
            }
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
