using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace Kirana.App;

/// <summary>
/// Relaunches Kirana. Needed after a restore: every <c>DbContext</c> in the running process — and
/// EF's model cache — is bound to the database file that was just replaced, so continuing in-process
/// would show a mix of old and new data.
/// </summary>
public static class AppRestartHelper
{
    public static void Restart()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executablePath))
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        }

        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
