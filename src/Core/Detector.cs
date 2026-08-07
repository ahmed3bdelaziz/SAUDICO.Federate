using System;
using System.IO;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.Core;

/// <summary>
/// Detects the type of Revit model (Local, Workshared, or Central) based on file analysis
/// </summary>
public static class Detector
{
    /// <summary>
    /// Determines the ModelKind of a Revit file by analyzing its structure
    /// </summary>
    /// <param name="filePath">Path to the .rvt file</param>
    /// <returns>ModelKind enum value (Local, WorksharedLocal, or Central)</returns>
    public static ModelKind Get(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return ModelKind.Local;

        try
        {
            // Check for central model marker in the file
            // Central models contain specific markers in their binary structure
            byte[] buffer = new byte[8192];
            
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // Read initial portion of file for analysis
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                
                // Search for central model indicators
                // Revit central models have specific binary signatures
                string content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                
                // Check for worksharing indicators
                bool hasWorksetIndicator = content.Contains("Workset") || content.Contains("Central");
                
                // More reliable: check file size and structure patterns
                // Central models typically have different internal structure
                if (HasCentralModelSignature(fs, filePath))
                    return ModelKind.Central;
                    
                if (HasWorksharedLocalSignature(filePath))
                    return ModelKind.WorksharedLocal;
            }
            
            return ModelKind.Local;
        }
        catch
        {
            // If we can't read the file, assume it's a local model
            return ModelKind.Local;
        }
    }
    
    /// <summary>
    /// Checks for central model signature by examining file metadata and backup patterns
    /// </summary>
    private static bool HasCentralModelSignature(FileStream fs, string filePath)
    {
        try
        {
            // Central models often have associated backup files with specific naming
            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            
            // Look for central model backup pattern (*.rvt.cdt or similar)
            string[] potentialBackups = Directory.GetFiles(directory, $"{fileName}.*");
            
            // Check if there's a central cache nearby
            foreach (var backup in potentialBackups)
            {
                string ext = Path.GetExtension(backup).ToLower();
                if (ext == ".cdt" || ext == ".dlt")
                    return true;
            }
            
            // Alternative: check file internals for central model GUID markers
            fs.Position = 0;
            byte[] header = new byte[4096];
            int read = fs.Read(header, 0, header.Length);
            
            // Scan for central model text markers in binary content
            for (int i = 0; i < read - 20; i++)
            {
                // Look for "Central" keyword in the binary stream
                if (header[i] == 'C' && header[i+1] == 'e' && header[i+2] == 'n' && 
                    header[i+3] == 't' && header[i+4] == 'r' && header[i+5] == 'a' && header[i+6] == 'l')
                    return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Checks if file is a workshared local copy (synchronized with central)
    /// </summary>
    private static bool HasWorksharedLocalSignature(string filePath)
    {
        try
        {
            // Workshared local files are typically in user workspace with specific naming
            // They may have synchronization metadata
            
            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            
            // Check for worksharing cache folders
            string[] cacheFolders = { "GLUF Cache", "RevitCache" };
            
            foreach (var cacheName in cacheFolders)
            {
                string cachePath = Path.Combine(directory, cacheName);
                if (Directory.Exists(cachePath))
                {
                    // Check if cache contains references to this file
                    var cacheFiles = Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories);
                    foreach (var cacheFile in cacheFiles)
                    {
                        if (cacheFile.Contains(fileName))
                            return true;
                    }
                }
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
}
