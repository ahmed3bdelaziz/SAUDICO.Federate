using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SAUDICO.Federate.Core;
using SAUDICO.Federate.Export;
using SAUDICO.Federate.Shared;
using SAUDICO.Federate.UI;

namespace SAUDICO.Federate.Revit2024
{
    public sealed class App : IExternalApplication
    {
        internal static RequestHandler Handler { get; private set; } = null!;
        internal static ExternalEvent ExternalEvent { get; private set; } = null!;

        public Autodesk.Revit.UI.Result OnStartup(UIControlledApplication application)
        {
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

            try
            {
                buttonData.LargeImage = new BitmapImage(
                    new Uri("pack://application:,,,/UI;component/Resources/icon32.png"));
                buttonData.Image = new BitmapImage(
                    new Uri("pack://application:,,,/UI;component/Resources/icon16.png"));
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
                window = new Manager(QueueJob);
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
