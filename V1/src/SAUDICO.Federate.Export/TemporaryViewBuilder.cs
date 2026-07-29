using System;
using System.Linq;
using Autodesk.Revit.DB;
using SAUDICO.Federate.Core.Interfaces;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.Export;

public sealed class TemporaryViewBuilder : ITemporaryViewBuilder
{
    public View3D Create(Document document)
    {
        using var transaction = new Transaction(
            document,
            "Create SAUDICO temporary federation view");

        try
        {
            transaction.Start();

            ElementId viewFamilyTypeId = new FilteredElementCollector(document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(type =>
                    type.ViewFamily == ViewFamily.ThreeDimensional)
                .Id;

            View3D view = View3D.CreateIsometric(
                document,
                viewFamilyTypeId);

            view.Name =
                Constants.TempViewPrefix +
                "-" +
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 8)
                    .ToUpperInvariant();

            // Some projects automatically assign a default template
            // to newly created 3D views. Remove that template only
            // from this new temporary export view.
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                view.ViewTemplateId = ElementId.InvalidElementId;

                // Update the view's parameter-control state before
                // applying the SAUDICO export settings.
                document.Regenerate();
            }

            if (!view.HasDetailLevel())
            {
                throw new InvalidOperationException(
                    "The temporary export view does not support " +
                    "the Detail Level parameter.");
            }

            if (!view.CanModifyDetailLevel())
            {
                throw new InvalidOperationException(
                    "The temporary export view Detail Level remains " +
                    "controlled and cannot be modified.");
            }

            view.DetailLevel = ViewDetailLevel.Fine;

            if (view.DetailLevel != ViewDetailLevel.Fine)
            {
                throw new InvalidOperationException(
                    "The temporary export view could not be " +
                    "configured to Fine detail level.");
            }

            view.Discipline = ViewDiscipline.Coordination;
            view.IsSectionBoxActive = false;

            foreach (Category category in document.Settings.Categories)
            {
                if (!category.get_AllowsVisibilityControl(view))
                {
                    continue;
                }

                bool shouldHide =
                    category.CategoryType == CategoryType.Annotation ||
                    category.CategoryType == CategoryType.AnalyticalModel;

                try
                {
                    view.SetCategoryHidden(
                        category.Id,
                        shouldHide);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Some categories cannot be controlled in this view.
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // Revit prevents some categories from being changed.
                }
            }

            TransactionStatus status = transaction.Commit();

            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException(
                    "Revit could not commit the temporary export view.");
            }

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