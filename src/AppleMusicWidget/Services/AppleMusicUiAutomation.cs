using System.Diagnostics;
using System.Windows.Automation;

namespace AppleMusicWidget.Services;

public static class AppleMusicUiAutomation
{
    private const string AppleMusicProcessName = "AppleMusic";

    public static Task ShowActionMenuAsync() =>
        RunOnWorker("ActionButton", el =>
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
                ((InvokePattern)pattern).Invoke();
            else
                Debug.WriteLine("[UIA] ActionButton does not support InvokePattern");
        });

    public static Task TogglePlayQueueAsync() =>
        RunOnWorker("PlayQueueToggleButton", el =>
        {
            if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
                ((TogglePattern)pattern).Toggle();
            else
                Debug.WriteLine("[UIA] PlayQueueToggleButton does not support TogglePattern");
        });

    private static Task RunOnWorker(string automationId, Action<AutomationElement> action) =>
        Task.Run(() =>
        {
            try
            {
                AppleMusicActivator.BringToFront();

                IntPtr hwnd = IntPtr.Zero;
                foreach (var p in Process.GetProcessesByName(AppleMusicProcessName))
                {
                    using (p)
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                            hwnd = p.MainWindowHandle;
                    }
                }
                if (hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine("[UIA] AppleMusic main window not found");
                    return;
                }

                var root = AutomationElement.FromHandle(hwnd);
                var el = root?.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
                if (el == null)
                {
                    Debug.WriteLine($"[UIA] element not found: {automationId}");
                    return;
                }
                if (!el.Current.IsEnabled)
                {
                    Debug.WriteLine($"[UIA] element disabled: {automationId}");
                    return;
                }
                action(el);
            }
            catch (Exception ex) { Debug.WriteLine($"[UIA] {automationId}: {ex.Message}"); }
        });
}
