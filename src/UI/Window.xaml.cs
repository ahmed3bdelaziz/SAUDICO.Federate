using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Export;
using SAUDICO.Federate.Shared;

namespace SAUDICO.Federate.UI;

public partial class Manager : Window, INotifyPropertyChanged
{
    private readonly Action<Job, Action<Result>, Action<string>> enqueue;
    private readonly SettingsService settingsService = new SettingsService();
    private readonly ApsAuthenticationService authenticationService;
    private string output = "";
    private string log = "Ready.";
    private ExportSettings settings;

    public ObservableCollection<Job> Jobs { get; } = new();
    public Array Params => Enum.GetValues(typeof(ParamMode));
    public Array Coords => Enum.GetValues(typeof(CoordMode));
    public Array Details => Enum.GetValues(typeof(DetailMode));
    public ExportSettings Settings { get => settings; set { settings = value; On(); } }
    public string Output { get => output; set { output = value; On(); } }
    public string Log { get => log; set { log = value; On(); } }

    public Manager(Action<Job, Action<Result>, Action<string>> queue)
    {
        InitializeComponent();
        enqueue = queue;
        settings = settingsService.Load();
        authenticationService = ApsAuthenticationServiceFactory.Create();
        AuthHost.Content = new AuthPanel(authenticationService);
        Closed += (_, _) => authenticationService.Dispose();
        DataContext = this;
    }

    private void Add(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog { Filter = "Revit (*.rvt)|*.rvt", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            foreach (string file in dialog.FileNames)
            {
                Jobs.Add(new Job { Source = file, SourceKind = SourceKind.LocalFile });
            }
        }
    }

    private void Remove(object sender, RoutedEventArgs e) { if (GridJobs.SelectedItem is Job job) Jobs.Remove(job); }
    private void Clear(object sender, RoutedEventArgs e) => Jobs.Clear();
    private void Folder(object sender, RoutedEventArgs e)
    {
        using System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) Output = dialog.SelectedPath;
    }
    private void Props(object sender, RoutedEventArgs e) { if (!Settings.Properties) Settings.Parameters = ParamMode.None; }
    private void Defaults(object sender, RoutedEventArgs e) => Settings = ExportSettings.Default();
    private void SaveDefault(object sender, RoutedEventArgs e) { settingsService.Save(Settings); Append("User default saved to settings.json"); }

    private void Run(object sender, RoutedEventArgs e)
    {
        settingsService.Save(Settings);
        if (Jobs.Count == 0 || !Directory.Exists(Output)) { MessageBox.Show("Add RVT files and select output folder."); return; }
        if (Settings.Faceting < .1 || Settings.Faceting > 10) { MessageBox.Show("Faceting Factor must be 0.10–10.00."); return; }
        int index = 0;
        Action next = null!;
        next = () =>
        {
            if (index >= Jobs.Count) { if (Settings.Csv) Append("CSV: " + Report.Write(Output, Jobs)); return; }
            Job job = Jobs[index++];
            job.OutputFolder = Output;
            job.Settings = Settings.Copy();
            enqueue(job,
                result => Dispatcher.Invoke(() => { job.Output = result.Path; job.Error = result.Error; job.State = result.Skipped ? State.Skipped : result.Ok ? State.Succeeded : State.Failed; next(); }),
                message => Dispatcher.Invoke(() => Append(job.Name + ": " + message)));
        };
        next();
    }

    private void Append(string message) => Log += Environment.NewLine + DateTime.Now.ToString("HH:mm:ss") + "  " + message;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void On([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
