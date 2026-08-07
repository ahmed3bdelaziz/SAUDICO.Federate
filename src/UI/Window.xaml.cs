using System;using System.Collections.Generic;using System.Collections.ObjectModel;using System.ComponentModel;using System.IO;using System.Linq;using System.Reflection;using System.Runtime.CompilerServices;using System.Windows;using System.Windows.Interop;using Microsoft.Win32;using SAUDICO.Federate.ACC.Authentication;using SAUDICO.Federate.ACC.Configuration;using SAUDICO.Federate.ACC.DataManagement;using SAUDICO.Federate.ACC.Http;using SAUDICO.Federate.Core;using SAUDICO.Federate.Export;using SAUDICO.Federate.Shared;namespace SAUDICO.Federate.UI;public partial class Manager:Window,INotifyPropertyChanged{readonly Action<Job,Action<Result>,Action<string>> enqueue;readonly SettingsService settingsService=new SettingsService();readonly Func<IApsAuthenticationService> authenticationServiceFactory;readonly IntPtr mainWindowHandle;readonly Assembly hostAssembly;readonly SingletonWindowSlot<DiagnosticShellWindow> level1Slot=new();readonly SingletonWindowSlot<AccBrowserWindow> accSlot=new();readonly System.Threading.CancellationTokenSource accDownloadCts=new();string output="",log="Ready.";ExportSettings settings;public ObservableCollection<Job> Jobs{get;}=new();public Array Params=>Enum.GetValues(typeof(ParamMode));public Array Coords=>Enum.GetValues(typeof(CoordMode));public Array Details=>Enum.GetValues(typeof(DetailMode));public ExportSettings Settings{get=>settings;set{settings=value;On();}}public string Output{get=>output;set{output=value;On();}}public string Log{get=>log;set{log=value;On();}}public Manager(Action<Job,Action<Result>,Action<string>> q,Func<IApsAuthenticationService> authFactory,IntPtr ownerHandle,Assembly hostAsm){InitializeComponent();enqueue=q;authenticationServiceFactory=authFactory;mainWindowHandle=ownerHandle;hostAssembly=hostAsm;settings=settingsService.Load();DataContext=this;}void Add(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Revit (*.rvt)|*.rvt",Multiselect=true};if(d.ShowDialog()==true)foreach(var f in d.FileNames)Jobs.Add(new Job{Source=f,Kind=Detector.Get(f)});}void Remove(object s,RoutedEventArgs e){if(GridJobs.SelectedItem is Job j)Jobs.Remove(j);}void Clear(object s,RoutedEventArgs e)=>Jobs.Clear();void Folder(object s,RoutedEventArgs e){using var d=new System.Windows.Forms.FolderBrowserDialog();if(d.ShowDialog()==System.Windows.Forms.DialogResult.OK)Output=d.SelectedPath;}void AddAccModels(object s,RoutedEventArgs e){LogMarker("AddAccModelsEntered");LogSafeAssemblyVersions();AccBrowserLevelRouter.Route(AccDiagnosticLevelSelection.Current,OpenLevel1DiagnosticShell,OpenLevel2AccBrowserWithoutAuthentication,OpenRealAuthenticatedAccBrowser);}void OpenLevel1DiagnosticShell(){if(level1Slot.TryActivateExisting(w=>w.Activate()))return;DiagnosticShellWindow window=new DiagnosticShellWindow();window.Closed+=delegate{level1Slot.Clear();};level1Slot.Set(window);LogMarker("BeforeOwnerAssignment");new WindowInteropHelper(window).Owner=mainWindowHandle;LogMarker("AfterOwnerAssignment");LogMarker("BeforeShow");window.Show();LogMarker("AfterShow");}void OpenLevel2AccBrowserWithoutAuthentication(){LogMarker("Level2Entered");if(accSlot.TryActivateExisting(w=>w.Activate()))return;AccBrowserWindow window=new AccBrowserWindow();window.Closed+=delegate{accSlot.Clear();};accSlot.Set(window);LogMarker("BeforeOwnerAssignment");new WindowInteropHelper(window).Owner=mainWindowHandle;LogMarker("AfterOwnerAssignment");LogMarker("BeforeShow");window.Show();LogMarker("AfterShow");}void OpenRealAuthenticatedAccBrowser(){if(accSlot.TryActivateExisting(w=>w.Activate()))return;AccBrowserLauncher.TryLaunch(authenticationServiceFactory,auth=>{AccBrowserWindow accBrowser=new AccBrowserWindow(auth,AddAccModelsToQueue);try{accBrowser.Owner=this;accBrowser.Closed+=delegate{accSlot.Clear();};accSlot.Set(accBrowser);accBrowser.Show();}catch{accBrowser.Close();accSlot.Clear();throw;}},message=>MessageBox.Show(message),DescribeSafeConfigDiagnostics);}
/// <summary>
/// Adds the selected ACC search-result rows to the existing federation
/// queue as Kind=Acc jobs. Never opens, downloads, or writes anything —
/// only queue-safe metadata is copied (AccCloudSourceDescriptor -&gt;
/// Core.AccCloudSource; Core deliberately never references the ACC
/// project, so the NWC exporter stays independent of APS). Deduplicates by
/// stable ItemId, not display name.
/// </summary>
void AddAccModelsToQueue(IReadOnlyList<AccBrowseNode> selected){
    int added=0,skipped=0;
    foreach(AccBrowseNode node in selected){
        AccCloudSourceDescriptor descriptor=AccCloudSourceDescriptor.FromSearchResult(node);
        if(AccQueueHelper.IsDuplicate(Jobs,descriptor.ItemId)){skipped++;continue;}
        Jobs.Add(new Job{
            Source=descriptor.DisplayName,
            Kind=ModelKind.Acc,
            AccSource=new AccCloudSource{
                Region=descriptor.Region,
                HubId=descriptor.HubId,
                HubName=descriptor.HubName,
                ProjectId=descriptor.ProjectId,
                ProjectName=descriptor.ProjectName,
                FolderId=descriptor.FolderId,
                FolderPath=descriptor.FolderPath,
                ItemId=descriptor.ItemId,
                VersionId=descriptor.VersionId,
                VersionNumber=descriptor.VersionNumber,
                ExtensionType=descriptor.ExtensionType,
                ProjectGuid=descriptor.ProjectGuid,
                ModelGuid=descriptor.ModelGuid,
                ResolutionStatus=descriptor.ResolutionStatus switch{
                    AccResolutionStatus.CloudModelVerified=>AccSourceResolutionStatus.CloudModelVerified,
                    AccResolutionStatus.UploadedFile=>AccSourceResolutionStatus.UploadedFile,
                    _=>AccSourceResolutionStatus.Unresolved,
                },
                ResolutionMessage=descriptor.ResolutionMessage,
            },
        });
        added++;
    }
    Append($"Added {added} ACC model(s) to the queue"+(skipped>0?$" ({skipped} already queued, skipped).":"."));
}static void LogMarker(string marker){try{Serilog.Log.Information("ACC diagnostic shell marker: {Marker}",marker);}catch{}}void LogSafeAssemblyVersions(){try{Assembly ui=typeof(Manager).Assembly;Assembly acc=typeof(IApsAuthenticationService).Assembly;Serilog.Log.Information("ACC diagnostic shell assemblies: HostPath={HostPath} HostVersion={HostVersion} UIPath={UIPath} UIVersion={UIVersion} AccPath={AccPath} AccVersion={AccVersion}",hostAssembly?.Location,hostAssembly?.GetName().Version?.ToString(),ui.Location,ui.GetName().Version?.ToString(),acc.Location,acc.GetName().Version?.ToString());}catch{}}static string DescribeSafeConfigDiagnostics(){string dir=ApsConfigurationService.GetDefaultConfigDirectory();bool baseExists=File.Exists(Path.Combine(dir,"apssettings.json"));bool localExists=File.Exists(Path.Combine(dir,"apssettings.local.json"));return $"baseExists={baseExists}, localExists={localExists}";}void Props(object s,RoutedEventArgs e){if(!Settings.Properties)Settings.Parameters=ParamMode.None;}void Defaults(object s,RoutedEventArgs e)=>Settings=ExportSettings.Default();void SaveDefault(object s,RoutedEventArgs e){settingsService.Save(Settings);Append("User default saved to settings.json");}async void Run(object s,RoutedEventArgs e){settingsService.Save(Settings);if(Jobs.Count==0||!Directory.Exists(Output)){MessageBox.Show("Add RVT files and select output folder.");return;}if(Settings.Faceting<.1||Settings.Faceting>10){MessageBox.Show("Faceting Factor must be 0.10–10.00.");return;}if(AccQueueHelper.HasBlockedAccJob(Jobs)){MessageBox.Show(AccQueueHelper.FirstBlockReason(Jobs)??"The selected ACC model cannot be exported.");return;}
    // Plain uploaded ACC files are not cloud models and cannot be opened by
    // GUID — they must be downloaded to a local copy first. This runs here,
    // on the UI thread's async context, deliberately NOT inside the Revit
    // ExternalEvent handler: a multi-hundred-megabyte download on the Revit
    // API context would freeze Revit for the whole transfer.
    if(!await DownloadQueuedAccFilesAsync())return;
    int i=0;Action next=null!;next=()=>{if(i>=Jobs.Count){if(Settings.Csv)Append("CSV: "+Report.Write(Output,Jobs));CleanUpAccTempFiles();return;}var j=Jobs[i++];if(j.State==State.Failed&&j.AccSource!=null&&j.Error!=null){next();return;}j.OutputFolder=Output;j.Settings=Settings.Copy();enqueue(j,res=>Dispatcher.Invoke(()=>{j.Output=res.Path;j.Error=res.Error;j.State=res.Skipped?State.Skipped:res.Ok?State.Succeeded:State.Failed;next();}),m=>Dispatcher.Invoke(()=>Append(j.Name+": "+m)));};next();}
/// <summary>
/// Downloads every queued plain-uploaded ACC file to a local temporary copy.
/// One failed download fails only that job and the batch continues, matching
/// the existing "one failed job must not stop the batch" rule. Returns false
/// only if the user cancelled the whole operation.
/// </summary>
async System.Threading.Tasks.Task<bool> DownloadQueuedAccFilesAsync(){
    IReadOnlyList<Job> pending=AccQueueHelper.JobsRequiringDownload(Jobs);
    if(pending.Count==0)return true;
    AccUploadedFileDownloader downloader;
    try{downloader=new AccUploadedFileDownloader(new AccDataManagementClient(new ApsHttpTransport(),authenticationServiceFactory()),new HttpAccBinaryDownloader());}
    catch{MessageBox.Show("SAUDICO Federate could not reach Autodesk to download the selected ACC file(s).");return false;}
    Append($"Downloading {pending.Count} uploaded ACC file(s) before export...");
    foreach(Job job in pending){
        try{
            string root=AccUploadedFileDownloader.DefaultCacheRoot;
            string path=await downloader.DownloadAsync(job.AccSource!.ProjectId,job.AccSource.VersionId,job.Name,root,null,accDownloadCts.Token);
            job.AccLocalCopyPath=path;job.AccTempDirectory=Path.GetDirectoryName(path);
            Append($"{job.Name}: downloaded.");
        }
        catch(OperationCanceledException){Append("ACC download cancelled.");return false;}
        catch(AccDownloadException ex){job.State=State.Failed;job.Error=ex.Message;Append($"{job.Name}: {ex.Message}");}
        catch{job.State=State.Failed;job.Error="This ACC file could not be downloaded.";Append($"{job.Name}: download failed.");}
    }
    return true;
}
void CleanUpAccTempFiles(){foreach(Job job in Jobs){if(job.AccTempDirectory!=null){AccUploadedFileDownloader.TryDeleteDirectory(job.AccTempDirectory);job.AccTempDirectory=null;job.AccLocalCopyPath=null;}}}void Append(string s)=>Log+=Environment.NewLine+DateTime.Now.ToString("HH:mm:ss")+"  "+s;public event PropertyChangedEventHandler? PropertyChanged;void On([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));}
