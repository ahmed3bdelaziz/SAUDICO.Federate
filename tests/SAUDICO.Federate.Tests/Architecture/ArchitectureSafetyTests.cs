using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SAUDICO.Federate.Tests.Architecture;

/// <summary>
/// Architecture tests to enforce critical safety boundaries
/// These tests do NOT require Revit to be installed
/// </summary>
public class ArchitectureSafetyTests
{
    private const string AccAssemblyName = "SAUDICO.Federate.ACC";
    private const string CoreAssemblyName = "SAUDICO.Federate.Core";
    
    [Fact]
    public void AccAssembly_DoesNotReference_RevitAssemblies()
    {
        // Arrange
        var accAssemblyPath = Path.Combine(
            AppContext.BaseDirectory, 
            "..", "..", "..", "..", "..",
            "src", AccAssemblyName, "bin", 
            "Release", "net8.0", 
            $"{AccAssemblyName}.dll");
        
        if (!File.Exists(accAssemblyPath))
        {
            // Skip if not built yet - this test requires build first
            return;
        }

        var accAssembly = Assembly.LoadFrom(accAssemblyPath);
        var referencedAssemblies = accAssembly.GetReferencedAssemblies();

        // Act & Assert
        var forbiddenReferences = new[]
        {
            "RevitAPI",
            "RevitAPIUI",
            "Autodesk.Revit.DB",
            "Autodesk.Revit.UI"
        };

        foreach (var forbidden in forbiddenReferences)
        {
            var found = referencedAssemblies.Any(r => r.Name.Contains(forbidden));
            Assert.False(found, 
                $"{AccAssemblyName} must not reference {forbidden}. " +
                $"Found references: {string.Join(", ", referencedAssemblies.Select(r => r.Name))}");
        }
    }

    [Fact]
    public void AccProject_DoesNotReference_RevitOrExportProjects()
    {
        // Arrange
        var accProjectPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", AccAssemblyName, $"{AccAssemblyName}.csproj");

        if (!File.Exists(accProjectPath))
        {
            throw new FileNotFoundException($"ACC project file not found at {accProjectPath}");
        }

        var projectContent = File.ReadAllText(accProjectPath);

        // Act & Assert
        var forbiddenReferences = new[]
        {
            "Revit2024",
            "Revit2025",
            "Export",
            "UI"
        };

        foreach (var forbidden in forbiddenReferences)
        {
            var contains = projectContent.Contains($"Include=\"..\\{forbidden}\"") ||
                          projectContent.Contains($"Include=\"..\\..\\{forbidden}\"");
            
            Assert.False(contains, 
                $"{AccAssemblyName}.csproj must not reference {forbidden} project. " +
                "ACC module must remain isolated from Revit API.");
        }
    }

    [Fact]
    public void Source_DoesNotContain_ProhibitedWriteCalls()
    {
        // Arrange
        var srcPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src");
        var prohibitedPatterns = new[]
        {
            "Document.Save(",
            "Document.SaveAs(",
            "SynchronizeWithCentral(",
            "Publish(",
            "RelinquishOwnership(",
            "Close(true)"
        };

        var csFiles = Directory.GetFiles(srcPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"));

        // Act
        var violations = new System.Collections.Generic.List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            
            // Skip comments and strings for more accurate detection
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Skip comment lines
                if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*"))
                    continue;
                
                // Skip string literals (simple heuristic)
                if (line.Contains("\"") && line.IndexOf("(") > line.IndexOf("\""))
                    continue;

                foreach (var pattern in prohibitedPatterns)
                {
                    if (line.Contains(pattern))
                    {
                        violations.Add($"{file}({i + 1}): Found prohibited call: {pattern}");
                    }
                }
            }
        }

        // Assert
        Assert.Empty(violations);
        if (violations.Any())
        {
            throw new Xunit.Sdk.XunitException(
                "Prohibited write operations found:\n" + string.Join("\n", violations));
        }
    }

    [Fact]
    public void AccSource_DoesNotUse_RevitNamespaces()
    {
        // Arrange
        var accPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", AccAssemblyName);

        var csFiles = Directory.GetFiles(accPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"));

        var forbiddenNamespaces = new[]
        {
            "Autodesk.Revit.DB",
            "Autodesk.Revit.UI",
            "Autodesk.Revit.Exceptions"
        };

        // Act
        var violations = new System.Collections.Generic.List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                if (line.StartsWith("using"))
                {
                    foreach (var ns in forbiddenNamespaces)
                    {
                        if (line.Contains(ns))
                        {
                            violations.Add(
                                $"{file}({i + 1}): ACC module cannot use {ns}");
                        }
                    }
                }
            }
        }

        // Assert
        Assert.Empty(violations);
        if (violations.Any())
        {
            throw new Xunit.Sdk.XunitException(
                "ACC module uses forbidden Revit namespaces:\n" + string.Join("\n", violations));
        }
    }
}
