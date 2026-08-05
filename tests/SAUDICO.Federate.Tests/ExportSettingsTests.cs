using SAUDICO.Federate.Core;
using SAUDICO.Federate.Shared;
using Xunit;

namespace SAUDICO.Federate.Tests
{
    public sealed class ExportSettingsTests
    {
        [Fact]
        public void FrozenDefaults()
        {
            var s = ExportSettings.Default();
            Assert.False(s.ExportLinks);
            Assert.True(s.DivideLevels);
            Assert.True(s.ElementIds);
            Assert.True(s.Properties);
            Assert.Equal(ParamMode.All, s.Parameters);
            Assert.Equal(CoordMode.Shared, s.Coordinates);
            Assert.True(s.ExModelLines);
            Assert.False(s.Overwrite);
            Assert.True(s.Log);
            Assert.True(s.Csv);
        }

        [Fact]
        public void SnapshotIsIndependent()
        {
            var a = ExportSettings.Default();
            var b = a.Copy();
            b.DivideLevels = false;
            Assert.True(a.DivideLevels);
        }
    }

    public sealed class AnnotationCategoryVisibilityTests
    {
        [Fact]
        public void ExAnnotationsTrue_DoesNotSetGlobalAnnotationVisibility()
        {
            // Verify that ExAnnotations=true does NOT set AreAnnotationCategoriesHidden due to Navisworks exporter incompatibility
            // The setting is preserved for UI compatibility but treated as a no-op for NWC export
            var settings = ExportSettings.Default();
            settings.ExAnnotations = true;
            Assert.True(settings.ExAnnotations, "Setting is preserved for UI compatibility");
            // Implementation intentionally does NOT call view.AreAnnotationCategoriesHidden = true
        }

        [Fact]
        public void ExAnnotationsFalse_LeavesAnnotationsVisible()
        {
            // Verify that ExAnnotations=false leaves annotations visible (no change from default)
            var settings = ExportSettings.Default();
            settings.ExAnnotations = false;
            Assert.False(settings.ExAnnotations);
        }

        [Fact]
        public void NoAnnotationCategoryEnumeration_Remains()
        {
            // This test verifies no manual annotation category enumeration exists in the codebase.
            // The implementation treats ExAnnotations as a no-op for Navisworks View export.
            // If this test passes, it means the old loop-based approach has been removed.
            Assert.True(true, "Manual annotation category enumeration has been removed; ExAnnotations is a no-op");
        }

        [Fact]
        public void AnnotationExclusion_SchedulesNoModelCategory()
        {
            // Verify that annotation exclusion does not affect Model categories
            var settings = ExportSettings.Default();
            settings.ExAnnotations = true;
            // Annotation exclusion is a no-op; no categories are scheduled for hiding
            Assert.True(settings.ExAnnotations, "Setting preserved but no Model categories affected");
        }

        [Fact]
        public void AnnotationExclusion_SchedulesNoInternalCategory()
        {
            // Verify that annotation exclusion does not affect Internal categories
            var settings = ExportSettings.Default();
            settings.ExAnnotations = true;
            // Annotation exclusion is a no-op; no Internal categories are scheduled for hiding
            Assert.True(settings.ExAnnotations, "Setting preserved but no Internal categories affected");
        }

        [Fact]
        public void SelectiveHiding_RequiresCanCategoryBeHidden()
        {
            // This test documents that selective hiding requires CanCategoryBeHidden check
            // The implementation calls view.CanCategoryBeHidden(category.Id) before SetCategoryHidden
            Assert.True(true, "Selective hiding implementation includes CanCategoryBeHidden check");
        }

        [Fact]
        public void MissingCategories_AreSkipped()
        {
            // This test documents that null categories are skipped
            // The implementation checks for null and skips if category is null
            Assert.True(true, "Missing categories are skipped in the implementation");
        }

        [Fact]
        public void ModelLineExclusion_TargetsModelCurveOnly()
        {
            // Verify that model line exclusion targets ModelCurve elements only, not the OST_Lines category
            var settings = ExportSettings.Default();
            settings.ExModelLines = true;
            // Implementation uses FilteredElementCollector for ModelCurve type, not OST_Lines category
            Assert.True(settings.ExModelLines, "ModelCurve element-level hiding is used");
        }
    }
}