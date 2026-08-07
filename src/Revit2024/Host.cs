using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
<<<<<<< HEAD
using SAUDICO.Federate.ACC.Authentication;
using SAUDICO.Federate.ACC.Callback;
using SAUDICO.Federate.ACC.Configuration;
using SAUDICO.Federate.ACC.Http;
using SAUDICO.Federate.ACC.OAuthState;
using SAUDICO.Federate.ACC.Pkce;
using SAUDICO.Federate.ACC.Profile;
using SAUDICO.Federate.ACC.Tokens;
=======
>>>>>>> 543bf77c1ff0ef3fc37e7324f43793331f6d5c4b
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Export;
using SAUDICO.Federate.Shared;
using SAUDICO.Federate.UI;
<<<<<<< HEAD

namespace SAUDICO.Federate.Revit2024
{
    public sealed class App : IExternalApplication
    {
        internal static RequestHandler Handler { get; private set; } = null!;
        internal static ExternalEvent ExternalEvent { get; private set; } = null!;
        private static IApsAuthenticationService? authenticationService;

        /// <summary>
        /// Lazily composes the APS object graph on first use. Deliberately NOT
        /// called from OnStartup: constructing this graph (and everything it
        /// touches — config file IO, HttpListener, DPAPI) must never happen
        /// as part of Revit application startup. Only the guarded
        /// "Add ACC Models" boundary invokes this.
        /// </summary>
        internal static IApsAuthenticationService ComposeAuthenticationService()
        {
            return authenticationService ??= new ApsAuthenticationService(
                new ApsConfigurationService(),
                new PkceService(),
                new InMemoryOAuthStateStore(),
                new LocalOAuthCallbackListener(),
                new ApsAuthorizationClient(new ApsHttpTransport()),
                new ApsUserProfileService(new ApsHttpTransport()),
                new DpapiApsTokenStore(),
                new SystemBrowserLauncher());
        }

        public Autodesk.Revit.UI.Result OnStartup(UIControlledApplication application)
        {
            try
            {
                SAUDICO.Federate.Logging.LoggingBootstrapper.Start();
                Serilog.Log.Information(
                    "SAUDICO Federate application started {RevitVersion} {AddInVersion}",
                    application.ControlledApplication.VersionNumber,
                    typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
            }
            catch
            {
                // A logging failure must never prevent the add-in from loading.
            }

            Handler = new RequestHandler();
            ExternalEvent = Autodesk.Revit.UI.ExternalEvent.Create(Handler);

            try
            {
                application.CreateRibbonTab(C.Tab);
            }
            catch
            {
                // The shared SAUDICO tab may already exist.
            }

            RibbonPanel panel = application
                .GetRibbonPanels(C.Tab)
                .FirstOrDefault(item => item.Name == C.Panel)
                ?? application.CreateRibbonPanel(C.Tab, C.Panel);

            PushButtonData buttonData = new PushButtonData(
                "SAUDICO.FederationManager",
                "Federation\nManager",
                typeof(App).Assembly.Location,
                typeof(FederationCommand).FullName);

            buttonData.ToolTip = "Open the SAUDICO Federation Manager.";
            buttonData.LongDescription = "Batch-export local and central Revit host models to Navisworks NWC using the SAUDICO Federation Export Standard.";

            try
            {
                buttonData.LargeImage = new BitmapImage(
                    new Uri("pack://application:,,,/SAUDICO.Federate.Revit2024;component/Resources/icon32.png"));
                buttonData.Image = new BitmapImage(
                    new Uri("pack://application:,,,/SAUDICO.Federate.Revit2024;component/Resources/icon16.png"));
            }
            catch
            {
                // The command remains usable if a ribbon image cannot be loaded.
            }

            panel.AddItem(buttonData);
            return Autodesk.Revit.UI.Result.Succeeded;
        }

        public Autodesk.Revit.UI.Result OnShutdown(UIControlledApplication application)
        {
            if (ExternalEvent != null)
            {
                ExternalEvent.Dispose();
            }

            try
            {
                Serilog.Log.Information("SAUDICO Federate application shutting down");
                SAUDICO.Federate.Logging.LoggingBootstrapper.Stop();
            }
            catch
            {
                // A logging failure must never prevent Revit from shutting down.
            }

            return Autodesk.Revit.UI.Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class FederationCommand : IExternalCommand
    {
        private static Manager? window;

        public Autodesk.Revit.UI.Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            if (window == null)
            {
                window = new Manager(QueueJob, App.ComposeAuthenticationService, commandData.Application.MainWindowHandle, typeof(App).Assembly);
                window.Closed += delegate { window = null; };
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();
            }
            else
            {
                window.Activate();
            }

            return Autodesk.Revit.UI.Result.Succeeded;
        }

        private static void QueueJob(
            Job job,
            Action<SAUDICO.Federate.Core.Result> completed,
            Action<string> log)
        {
            App.Handler.Enqueue(new RevitWorkItem(job, completed, log));
            App.ExternalEvent.Raise();
        }
    }

    public sealed class RevitWorkItem
    {
        public RevitWorkItem(
            Job job,
            Action<SAUDICO.Federate.Core.Result> completed,
            Action<string> log)
        {
            Job = job;
            Completed = completed;
            Log = log;
        }

        public Job Job { get; }
        public Action<SAUDICO.Federate.Core.Result> Completed { get; }
        public Action<string> Log { get; }
    }

    public sealed class RequestHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<RevitWorkItem> queue =
            new ConcurrentQueue<RevitWorkItem>();

        public void Enqueue(RevitWorkItem item)
        {
            queue.Enqueue(item);
        }

        public void Execute(UIApplication application)
        {
            RevitWorkItem item;
            if (!queue.TryDequeue(out item))
            {
                return;
            }

            SAUDICO.Federate.Core.Result result =
                Engine.Run(item.Job, application.Application, item.Log);

            item.Completed(result);
        }

        public string GetName()
        {
            return "SAUDICO Federate FIFO request handler";
        }
    }
}
=======
>>>>>>> 543bf77c1ff0ef3fc37e7324f43793331f6d5c4b

namespace SAUDICO.Federate.Revit2024
{
    public sealed class App : IExternalApplication
    {
        internal static RequestHandler Handler { get; private set; } = null!;
        internal static ExternalEvent ExternalEvent { get; private set; } = null!;

        public Autodesk.Revit.UI.Result OnStartup(UIControlledApplication application)
        {
            try
            {
                SAUDICO.Federate.Logging.LoggingBootstrapper.Start();
                Serilog.Log.Information(
                    "SAUDICO Federate application started {RevitVersion} {AddInVersion}",
                    application.ControlledApplication.VersionNumber,
                    typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
            }
            catch
            {
                // A logging failure must never prevent the add-in from loading.
            }

            Handler = new RequestHandler();
            ExternalEvent = Autodesk.Revit.UI.ExternalEvent.Create(Handler);

            try
            {
                application.CreateRibbonTab(C.Tab);
            }
            catch
            {
                // The shared SAUDICO tab may already exist.
            }

            RibbonPanel panel = application
                .GetRibbonPanels(C.Tab)
                .FirstOrDefault(item => item.Name == C.Panel)
                ?? application.CreateRibbonPanel(C.Tab, C.Panel);

            PushButtonData buttonData = new PushButtonData(
                "SAUDICO.FederationManager",
                "Federation\nManager",
                typeof(App).Assembly.Location,
                typeof(FederationCommand).FullName);

            buttonData.ToolTip = "Open the SAUDICO Federation Manager.";
            buttonData.LongDescription = "Batch-export local and central Revit host models to Navisworks NWC using the SAUDICO Federation Export Standard.";

            try
            {
                buttonData.LargeImage = new BitmapImage(
                    new Uri("pack://application:,,,/SAUDICO.Federate.Revit2024;component/Resources/icon32.png"));
                buttonData.Image = new BitmapImage(
                    new Uri("pack://application:,,,/SAUDICO.Federate.Revit2024;component/Resources/icon16.png"));
            }
            catch
            {
                // The command remains usable if a ribbon image cannot be loaded.
            }

            panel.AddItem(buttonData);
            return Autodesk.Revit.UI.Result.Succeeded;
        }

        public Autodesk.Revit.UI.Result OnShutdown(UIControlledApplication application)
        {
            if (ExternalEvent != null)
            {
                ExternalEvent.Dispose();
            }

            try
            {
                Serilog.Log.Information("SAUDICO Federate application shutting down");
                SAUDICO.Federate.Logging.LoggingBootstrapper.Stop();
            }
            catch
            {
                // A logging failure must never prevent Revit from shutting down.
            }

            return Autodesk.Revit.UI.Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class FederationCommand : IExternalCommand
    {
        private static Manager? window;

        public Autodesk.Revit.UI.Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            if (window == null)
            {
                var authService = SAUDICO.Federate.ACC.Authentication.ApsAuthenticationService.CreateDefault();
                window = new Manager(QueueJob, authService);
                window.Closed += delegate { window?.Dispose(); window = null; };
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();
            }
            else
            {
                window.Activate();
            }

            return Autodesk.Revit.UI.Result.Succeeded;
        }

        private static void QueueJob(
            Job job,
            Action<SAUDICO.Federate.Core.Result> completed,
            Action<string> log)
        {
            App.Handler.Enqueue(new RevitWorkItem(job, completed, log));
            App.ExternalEvent.Raise();
        }
    }

    public sealed class RevitWorkItem
    {
        public RevitWorkItem(
            Job job,
            Action<SAUDICO.Federate.Core.Result> completed,
            Action<string> log)
        {
            Job = job;
            Completed = completed;
            Log = log;
        }

        public Job Job { get; }
        public Action<SAUDICO.Federate.Core.Result> Completed { get; }
        public Action<string> Log { get; }
    }

    public sealed class RequestHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<RevitWorkItem> queue =
            new ConcurrentQueue<RevitWorkItem>();

        public void Enqueue(RevitWorkItem item)
        {
            queue.Enqueue(item);
        }

        public void Execute(UIApplication application)
        {
            RevitWorkItem item;
            if (!queue.TryDequeue(out item))
            {
                return;
            }

            // Classify file type on Revit API thread using BasicFileInfo.Extract
            // This avoids calling Revit API from UI thread while properly detecting central models
            if (item.Job.SourceKind == SourceKind.LocalFile && !string.IsNullOrEmpty(item.Job.LocalFilePath))
            {
                try
                {
                    var fileInfo = Autodesk.Revit.DB.BasicFileInfo.Extract(item.Job.LocalFilePath);
                    if (fileInfo != null)
                    {
                        if (fileInfo.IsCentral)
                        {
                            item.Job.SourceKind = SourceKind.FileCentral;
                            item.Log($"Detected as central model: {item.Job.Name}");
                        }
                        else if (fileInfo.IsWorkshared)
                        {
                            item.Job.SourceKind = SourceKind.FileCentral;
                            item.Log($"Detected as workshared local copy: {item.Job.Name}");
                        }
                        else
                        {
                            item.Log($"Detected as local model: {item.Job.Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    item.Log($"Warning: Could not classify {item.Job.Name}: {ex.Message}");
                    // Continue with LocalFile assumption
                }
            }

            SAUDICO.Federate.Core.Result result =
                Engine.Run(item.Job, application.Application, item.Log);

            item.Completed(result);
        }

        public string GetName()
        {
            return "SAUDICO Federate FIFO request handler";
        }
    }
}
