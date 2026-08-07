using System;
using System.IO;

namespace SAUDICO.Federate.Core
{
    /// <summary>
    /// Handles atomic file replacement with backup/rollback strategy.
    /// Executes AFTER Revit document is closed. Pure C# - No Revit API.
    /// </summary>
    public static class FileFinalizer
    {
        public struct FinalizeResult
        {
            public bool Success;
            public string? FinalPath;
            public string? BackupPath;
            public string? ErrorMessage;
        }

        public static FinalizeResult Execute(string stagingPath, string destinationPath)
        {
            if (!File.Exists(stagingPath))
                return new FinalizeResult { Success = false, ErrorMessage = "Staging artifact not found." };

            try
            {
                // If destination doesn't exist, simple move
                if (!File.Exists(destinationPath))
                {
                    File.Move(stagingPath, destinationPath);
                    return new FinalizeResult { Success = true, FinalPath = destinationPath };
                }

                // Destination exists: Backup -> Replace -> Verify
                string backupPath = destinationPath + ".backup";
                
                // 1. Backup existing
                File.Move(destinationPath, backupPath);
                
                try
                {
                    // 2. Move new file
                    File.Move(stagingPath, destinationPath);
                    
                    // 3. Verify new file
                    if (new FileInfo(destinationPath).Length == 0)
                        throw new IOException("Finalized file is empty.");

                    // 4. Delete backup on success
                    File.Delete(backupPath);
                    
                    return new FinalizeResult { Success = true, FinalPath = destinationPath };
                }
                catch
                {
                    // Rollback: Restore backup if replace fails
                    if (File.Exists(backupPath))
                        File.Move(backupPath, destinationPath);
                    throw;
                }
            }
            catch (Exception ex)
            {
                return new FinalizeResult { 
                    Success = false, 
                    ErrorMessage = $"Finalization failed: {ex.Message}",
                    BackupPath = File.Exists(destinationPath + ".backup") ? destinationPath + ".backup" : null
                };
            }
        }
    }
}
