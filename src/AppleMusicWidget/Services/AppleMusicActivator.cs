using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AppleMusicWidget.Services;

/// <summary>
/// Brings the Apple Music window to the foreground (PLAN: Apple Musicへの導線).
/// Store apps report no MainWindowHandle while minimized, so fall back to
/// relaunching the AUMID which just re-activates the running instance.
/// </summary>
public static class AppleMusicActivator
{
    private const string AppleMusicProcessName = "AppleMusic";
    private const string AppleMusicAumid = @"shell:AppsFolder\AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";

    public static void BringToFront()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(AppleMusicProcessName))
            {
                using (p)
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
                    SetForegroundWindow(hwnd);
                    return;
                }
            }
            // No usable window handle (minimized Store app): re-activate via AUMID.
            Process.Start(new ProcessStartInfo(AppleMusicAumid) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Debug.WriteLine($"[Activator] BringToFront failed: {ex.Message}"); }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
