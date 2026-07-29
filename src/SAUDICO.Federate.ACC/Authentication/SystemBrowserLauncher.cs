using System;
using System.Diagnostics;

namespace SAUDICO.Federate.ACC.Authentication;

public sealed class SystemBrowserLauncher : ISystemBrowserLauncher
{
    public void Launch(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
