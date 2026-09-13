using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using AppleMusicWidget.Models;

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

    public static Task<IReadOnlyList<PlayQueueItem>?> ReadPlayQueueAsync() =>
        Task.Run<IReadOnlyList<PlayQueueItem>?>(() =>
        {
            var previousForeground = Native.GetForegroundWindow();
            var openedByUs = false;
            TogglePattern? toggle = null;
            try
            {
                var root = FindRoot();
                if (root == null)
                {
                    Debug.WriteLine("[UIA] PlayQueue: AppleMusic main window not found");
                    return null;
                }
                var button = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayQueueToggleButton"));
                if (button == null)
                {
                    Debug.WriteLine("[UIA] PlayQueue: element not found: PlayQueueToggleButton");
                    return null;
                }
                if (!button.Current.IsEnabled)
                {
                    Debug.WriteLine("[UIA] PlayQueue: element disabled: PlayQueueToggleButton");
                    return null;
                }
                if (!button.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
                {
                    Debug.WriteLine("[UIA] PlayQueue: PlayQueueToggleButton does not support TogglePattern");
                    return null;
                }
                toggle = (TogglePattern)pattern;
                if (toggle.Current.ToggleState == ToggleState.Off)
                {
                    toggle.Toggle();
                    openedByUs = true;
                    RestoreForeground(previousForeground);
                }

                AutomationElement? list = null;
                for (var i = 0; i < 30 && list == null; i++)
                {
                    if (i > 0) Thread.Sleep(50);
                    list = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayQueueListView"));
                }
                if (list == null)
                {
                    Debug.WriteLine("[UIA] PlayQueue: PlayQueueListView not found");
                    return null;
                }
                if (!list.TryGetCurrentPattern(ItemContainerPattern.Pattern, out var containerPattern))
                {
                    Debug.WriteLine("[UIA] PlayQueue: PlayQueueListView does not support ItemContainerPattern");
                    return null;
                }

                var container = (ItemContainerPattern)containerPattern;
                var items = new List<PlayQueueItem>();
                AutomationElement? current = null;
                while (items.Count < 200)
                {
                    current = container.FindItemByProperty(current, null!, null);
                    if (current is null) break;
                    var texts = current.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                    var values = new List<string>();
                    for (var i = 0; i < texts.Count; i++)
                    {
                        var value = texts[i].Current.Name;
                        if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                    }
                    if (values.Count > 0)
                        items.Add(new PlayQueueItem(values[0], values.Count > 1 ? values[1] : "", values.Count > 2 ? values[2] : ""));
                }
                return items;
            }
            catch (Exception ex) { Debug.WriteLine($"[UIA] PlayQueue: {ex.Message}"); return null; }
            finally
            {
                try
                {
                    if (openedByUs && toggle?.Current.ToggleState == ToggleState.On)
                    {
                        toggle.Toggle();
                        RestoreForeground(previousForeground);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[UIA] PlayQueue finally: {ex.Message}"); }
            }
        });

    private static AutomationElement? FindRoot()
    {
        foreach (var p in Process.GetProcessesByName(AppleMusicProcessName))
        {
            using (p)
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                    return AutomationElement.FromHandle(p.MainWindowHandle);
            }
        }
        return null;
    }

    private static void RestoreForeground(IntPtr previous)
    {
        if (previous == IntPtr.Zero) return;
        if (Native.GetForegroundWindow() == previous) return;
        if (!Native.SetForegroundWindow(previous))
            Debug.WriteLine("[UIA] SetForegroundWindow failed");
    }

    private static Task RunOnWorker(string automationId, Action<AutomationElement> action) =>
        Task.Run(() =>
        {
            try
            {
                AppleMusicActivator.BringToFront();
                var root = FindRoot();
                if (root == null)
                {
                    Debug.WriteLine("[UIA] AppleMusic main window not found");
                    return;
                }
                var el = root.FindFirst(TreeScope.Descendants,
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

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
