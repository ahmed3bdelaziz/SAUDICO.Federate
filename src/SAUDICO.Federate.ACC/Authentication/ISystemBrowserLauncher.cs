using System;

namespace SAUDICO.Federate.ACC.Authentication;

/// <summary>Launches the user's default system browser. Never an embedded browser control.</summary>
public interface ISystemBrowserLauncher
{
    void Launch(Uri uri);
}
