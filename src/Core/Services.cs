using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Serilog;

namespace SAUDICO.Federate.Core;

/// <summary>
/// Document opening service supporting Local, Central, and ACC Cloud models
/// Implements Revit API best practices for error handling and validation
/// </summary>
public static class Opener
{
    /// <summary>
    /// Opens a document with appropriate options based on source type
    /// </summary>
    public static Document Open(Job job, Application application)
    {
        // Validate source configuration
        if (!job.ValidateSource(out var validationError))
            throw new ArgumentException(validationError, nameof(job));
        
        ModelPath modelPath;
        OpenOptions options = new OpenOptions();
        
        // Configure based on source kind
        if (job.SourceKind == SourceKind.AccCloudModel)
        {
            // ACC Cloud Model - requires DoNotDetach
            if (job.CloudDescriptor == null)
                throw new InvalidOperationException("Cloud descriptor is null for AccCloudModel");
            
            Log.Information("Opening ACC cloud model: {DisplayName} (Project: {ProjectGuid}, Model: {ModelGuid})", 
                job.CloudDescriptor.DisplayName, job.CloudDescriptor.ProjectGuid, job.CloudDescriptor.ModelGuid);
            
            try
            {
                modelPath = ModelPathUtils.ConvertCloudGUIDsToCloudPath(
                    job.CloudDescriptor.Region,
                    job.CloudDescriptor.ProjectGuid,
                    job.CloudDescriptor.ModelGuid);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to convert cloud GUIDs to model path for {DisplayName}", job.CloudDescriptor.DisplayName);
                throw new InvalidOperationException($"Invalid cloud model path: {ex.Message}", ex);
            }
            
            // CRITICAL: Cloud documents MUST use DoNotDetach
            options.DetachFromCentralOption = DetachFromCentralOption.DoNotDetach;
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        }
        else if (job.SourceKind == SourceKind.FileCentral)
        {
            // File-based Central Model - detach and preserve worksets
            if (string.IsNullOrEmpty(job.LocalFilePath))
                throw new InvalidOperationException("Local file path is null for FileCentral");
            
            Log.Information("Opening file-based central model: {Path}", job.LocalFilePath);
            
            if (!File.Exists(job.LocalFilePath))
                throw new FileNotFoundException($"Central model not found: {job.LocalFilePath}");
            
            modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(job.LocalFilePath);
            
            if (modelPath == null)
                throw new InvalidOperationException($"Invalid model path: {job.LocalFilePath}");
            
            // Detach from central but preserve worksets for coordination
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        }
        else
        {
            // Local File - open normally without detaching
            if (string.IsNullOrEmpty(job.LocalFilePath))
                throw new InvalidOperationException("Local file path is null for LocalFile");
            
            Log.Information("Opening local model: {Path}", job.LocalFilePath);
            
            if (!File.Exists(job.LocalFilePath))
                throw new FileNotFoundException($"Local model not found: {job.LocalFilePath}");
            
            modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(job.LocalFilePath);
            
            if (modelPath == null)
                throw new InvalidOperationException($"Invalid model path: {job.LocalFilePath}");
            
            // Local files don't need detach options
        }
        
        // Attempt to open the document with proper error handling
        try
        {
            Document document = application.OpenDocumentFile(modelPath, options);
            
            if (document == null)
                throw new InvalidOperationException($"Failed to open document: {job.Source}");
            
            if (!document.IsValidObject)
                throw new InvalidOperationException($"Opened document is invalid: {job.Source}");
            
            Log.Information("Successfully opened {Kind} model: {Source}", job.SourceKind, job.Source);
            return document;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            Log.Error(ex, "Revit API failed to open {Source} ({Kind})", job.Source, job.SourceKind);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error opening {Source} ({Kind})", job.Source, job.SourceKind);
            throw new InvalidOperationException($"Failed to open document: {ex.Message}", ex);
        }
    }
}
