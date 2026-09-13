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

    public static Task<PlayQueueSnapshot?> ReadPlayQueueAsync() =>
        Task.Run<PlayQueueSnapshot?>(() =>
        {
            var previousForeground = Native.GetForegroundWindow();
            var openedByUs = false;
            var historyWasSelected = false;
            AutomationElement? root = null;
            try
            {
                root = FindRoot();
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
                var toggle = (TogglePattern)pattern;
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

                var initialTabs = FindQueueTabs(root);
                if (initialTabs is null)
                {
                    Debug.WriteLine("[UIA] PlayQueue: queue tabs not found");
                    return null;
                }
                historyWasSelected = initialTabs.Value.History.Current.ToggleState == ToggleState.On;

                if (!SelectQueueTab(root, history: false, previousForeground)) return null;
                var upcoming = ReadQueueItems(root, 200);
                if (upcoming is null) return null;
                if (!SelectQueueTab(root, history: true, previousForeground)) return null;
                var history = ReadQueueItems(root, 50);
                if (history is null) return null;
                return new PlayQueueSnapshot(history, upcoming);
            }
            catch (Exception ex) { Debug.WriteLine($"[UIA] PlayQueue: {ex.Message}"); return null; }
            finally
            {
                try
                {
                    if (root != null)
                        SelectQueueTab(root, historyWasSelected, previousForeground);
                }
                catch (Exception ex) { Debug.WriteLine($"[UIA] PlayQueue tab restore: {ex.Message}"); }
                try
                {
                    if (openedByUs && root != null)
                    {
                        var b = root.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayQueueToggleButton"));
                        if (b?.TryGetCurrentPattern(TogglePattern.Pattern, out var tp) == true)
                        {
                            var t = (TogglePattern)tp;
                            if (t.Current.ToggleState == ToggleState.On)
                            {
                                t.Toggle();
                                RestoreForeground(previousForeground);
                            }
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[UIA] PlayQueue finally: {ex.Message}"); }
            }
        });

    private static (TogglePattern Next, TogglePattern History)? FindQueueTabs(AutomationElement root)
    {
        var list = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayQueueListView"));
        if (list == null) return null;
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? node = list;
        AutomationElement? pane = null;
        while (node != null)
        {
            node = walker.GetParent(node);
            if (node?.Current.AutomationId == "PaneRoot") { pane = node; break; }
        }
        if (pane == null) return null;
        var children = pane.FindAll(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleButton")));
        var ordered = children.Cast<AutomationElement>()
            .OrderBy(c => c.Current.BoundingRectangle.Left).ToList();
        if (ordered.Count < 2) return null;
        if (!ordered[0].TryGetCurrentPattern(TogglePattern.Pattern, out var nextPattern)) return null;
        if (!ordered[1].TryGetCurrentPattern(TogglePattern.Pattern, out var historyPattern)) return null;
        return ((TogglePattern)nextPattern, (TogglePattern)historyPattern);
    }

    private static bool SelectQueueTab(AutomationElement root, bool history, IntPtr previousForeground)
    {
        var tabs = FindQueueTabs(root);
        if (tabs is null)
        {
            Debug.WriteLine("[UIA] PlayQueue: tabs not found");
            return false;
        }
        var target = history ? tabs.Value.History : tabs.Value.Next;
        if (target.Current.ToggleState == ToggleState.On) return true;
        target.Toggle();
        RestoreForeground(previousForeground);
        Thread.Sleep(250);
        var fresh = FindQueueTabs(root);
        if (fresh is null) return false;
        var freshTarget = history ? fresh.Value.History : fresh.Value.Next;
        return freshTarget.Current.ToggleState == ToggleState.On;
    }

    private static IReadOnlyList<PlayQueueItem>? ReadQueueItems(AutomationElement root, int limit)
    {
        var list = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "PlayQueueListView"));
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
        while (items.Count < limit)
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
