using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using MultiDesktopAdaptor.Models;
using MultiDesktopAdaptor.Services;
using WindowsDesktop;
using Application = System.Windows.Application;

namespace MultiDesktopAdaptor;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private DesktopMonitorService? _desktopMonitor;
    private AppConfiguration _configuration = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configuration = ConfigurationService.Load();

        _desktopMonitor = new DesktopMonitorService();
        _desktopMonitor.DesktopSwitched += OnDesktopSwitched;
        _desktopMonitor.Start();

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.IconSource = CreateImageSourceFromIcon(SystemIcons.Application);
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowConfigurationWindow();

        RebuildTrayMenu();
    }

    private void RebuildTrayMenu(List<(Process process, string title)>? mismatched = null)
    {
        if (_trayIcon?.ContextMenu == null)
            return;

        var menu = _trayIcon.ContextMenu;
        menu.Items.Clear();

        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Show Configuration",
            Command = new RelayCommand(ShowConfigurationWindow)
        });

        // Commands submenu
        if (_configuration.Commands.Count > 0)
        {
            var commandsMenu = new System.Windows.Controls.MenuItem { Header = "Commands" };
            foreach (var cmd in _configuration.Commands)
            {
                var label = string.IsNullOrWhiteSpace(cmd.Title) ? cmd.CommandLine : cmd.Title;
                var item = new System.Windows.Controls.MenuItem { Header = label };
                item.Click += (_, _) => ExecuteCommand(cmd);
                commandsMenu.Items.Add(item);
            }
            menu.Items.Add(commandsMenu);
        }

        // Mismatched processes (data pre-computed on background thread)
        if (mismatched is { Count: > 0 })
        {
            var mismatchedMenu = new System.Windows.Controls.MenuItem { Header = "Mismatched" };
            foreach (var (process, title) in mismatched)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = $"{process.ProcessName} — {title}",
                    Tag = process
                };
                item.Click += (_, e) =>
                {
                    try
                    {
                        if (e.Source is System.Windows.Controls.MenuItem mi && mi.Tag is Process proc)
                            proc.Kill();
                    }
                    catch { }
                };
                mismatchedMenu.Items.Add(item);
            }
            menu.Items.Add(mismatchedMenu);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Exit",
            Command = new RelayCommand(ShutdownApplication)
        });
    }

    private CancellationTokenSource? _applyRulesCts;

    private void OnDesktopSwitched(object? sender, DesktopSwitchEventArgs e)
    {
        // Capture the target desktop immediately on the event thread
        var targetDesktop = VirtualDesktop.Current;

        // Cancel any in-progress work from a previous switch
        _applyRulesCts?.Cancel();
        _applyRulesCts = new CancellationTokenSource();
        var token = _applyRulesCts.Token;

        var eventTs = Stopwatch.GetTimestamp();
        Logger.Info("[DesktopSwitch] event fired, dispatching background work...");

        // All heavy work on background thread; only UI mutations on UI thread
        Task.Run(() =>
        {
            var totalSw = Stopwatch.StartNew();
            var scheduleDelayMs = (Stopwatch.GetTimestamp() - eventTs) * 1000.0 / Stopwatch.Frequency;
            Logger.Info($"[DesktopSwitch] bg task started, scheduleDelay={scheduleDelayMs:F0}ms");

            // --- Phase 1: Window enumeration + rule matching (background) ---
            List<IntPtr>? windowsToMove = null;
            var windowCount = 0;
            if (_configuration.FollowRules.Count > 0)
            {
                var enumSw = Stopwatch.StartNew();
                var windowInfos = EnumerateWindowInfos();
                enumSw.Stop();
                windowCount = windowInfos.Count;

                token.ThrowIfCancellationRequested();

                var matchSw = Stopwatch.StartNew();
                windowsToMove = new List<IntPtr>();
                foreach (var rule in _configuration.FollowRules)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var wi in windowInfos)
                    {
                        if (MatchesRule(wi, rule))
                            windowsToMove.Add(wi.Hwnd);
                    }
                }
                matchSw.Stop();
                Logger.Info($"[ApplyRules] windows={windowCount} rules={_configuration.FollowRules.Count} matched={windowsToMove.Count} | enum={enumSw.ElapsedMilliseconds}ms match={matchSw.ElapsedMilliseconds}ms");
            }

            // --- Phase 2: Move matched windows (background, cancellable) ---
            var movedCount = 0;
            if (windowsToMove is { Count: > 0 })
            {
                var moveSw = Stopwatch.StartNew();
                foreach (var hwnd in windowsToMove)
                {
                    token.ThrowIfCancellationRequested();
                    try { VirtualDesktop.MoveToDesktop(hwnd, targetDesktop); movedCount++; }
                    catch { }
                }
                moveSw.Stop();
                Logger.Info($"[MoveWindows] attempted={windowsToMove.Count} moved={movedCount} | {moveSw.ElapsedMilliseconds}ms");
            }

            // --- Phase 3: Mismatched process detection (background) ---
            token.ThrowIfCancellationRequested();
            var mismatchSw = Stopwatch.StartNew();
            var mismatched = GetMismatchedProcesses();
            mismatchSw.Stop();

            // --- Phase 4: Dispatch menu rebuild to UI thread (async, non-blocking) ---
            token.ThrowIfCancellationRequested();
            var beforeDispatch = Stopwatch.GetTimestamp();
            Dispatcher.BeginInvoke(() =>
            {
                var dispatchDelayMs = (Stopwatch.GetTimestamp() - beforeDispatch) * 1000.0 / Stopwatch.Frequency;
                Logger.Info($"[RebuildMenu] dispatchDelay={dispatchDelayMs:F0}ms");
                RebuildTrayMenu(mismatched);
            });

            totalSw.Stop();
            Logger.Info($"[DesktopSwitch] bgTotal={totalSw.ElapsedMilliseconds}ms | windows={windowCount} rules={_configuration.FollowRules.Count} matched={windowsToMove?.Count ?? 0} moved={movedCount} mismatched={mismatched.Count}");
        }, token);
    }

    #region Command execution & variables

    /// <summary>
    /// Resolves a variable's value for the current desktop.
    /// Desktop-specific value takes priority; falls back to default.
    /// </summary>
    private static string ResolveVariable(VariableDefinition variable)
    {
        var desktopId = VirtualDesktop.Current.Id;
        if (variable.DesktopValues.TryGetValue(desktopId, out var desktopValue) && !string.IsNullOrWhiteSpace(desktopValue))
            return desktopValue;
        return variable.DefaultValue;
    }

    /// <summary>
    /// Replaces $VarName tokens in a command line with resolved variable values.
    /// </summary>
    private string ResolveCommandLine(CommandDefinition command)
    {
        var result = command.CommandLine;
        foreach (var variable in _configuration.Variables)
        {
            result = result.Replace($"${variable.Name}", ResolveVariable(variable), StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>
    /// Executes a command with variables resolved for the current desktop.
    /// </summary>
    public void ExecuteCommand(CommandDefinition command)
    {
        var resolved = ResolveCommandLine(command);
        if (string.IsNullOrWhiteSpace(resolved))
            return;

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{resolved}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process != null)
                _commandProcesses.Add(process);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExecuteCommand] Failed: {ex.Message}");
        }
    }

    private readonly List<Process> _commandProcesses = new();

    /// <summary>
    /// Returns command-launched processes whose main window is NOT on the current desktop.
    /// </summary>
    private List<(Process process, string title)> GetMismatchedProcesses()
    {
        var result = new List<(Process, string)>();
        var currentDesktop = VirtualDesktop.Current;
        var dead = new List<Process>();

        long refreshMs = 0, comMs = 0;
        var sw = Stopwatch.StartNew();

        foreach (var proc in _commandProcesses)
        {
            try
            {
                if (proc.HasExited) { dead.Add(proc); continue; }

                var rSw = Stopwatch.StartNew();
                proc.Refresh();
                refreshMs += rSw.ElapsedMilliseconds;

                var hwnd = proc.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;

                var cSw = Stopwatch.StartNew();
                var desktop = VirtualDesktop.FromHwnd(hwnd);
                comMs += cSw.ElapsedMilliseconds;

                if (desktop?.Id != currentDesktop.Id)
                    result.Add((proc, proc.MainWindowTitle));
            }
            catch { dead.Add(proc); }
        }

        foreach (var d in dead) _commandProcesses.Remove(d);

        Logger.Info($"[Mismatched] processes={_commandProcesses.Count} mismatched={result.Count} refresh={refreshMs}ms com={comMs}ms total={sw.ElapsedMilliseconds}ms");
        return result;
    }

    #endregion

    private static bool MatchesRule(WindowInfo wi, WindowRule rule)
    {
        if (rule.WindowTitle != null)
        {
            if (!wi.Title.Contains(rule.WindowTitle, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (rule.WindowClassName != null)
        {
            if (!wi.ClassName.Equals(rule.WindowClassName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (rule.ProcessName != null)
        {
            if (!wi.ProcessName.Equals(rule.ProcessName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private readonly record struct WindowInfo(IntPtr Hwnd, string Title, string ClassName, string ProcessName);

    /// <summary>
    /// Enumerates all top-level windows once, collecting title/class/process name
    /// in a single pass. Uses a single Process.GetProcesses() batch call to build
    /// a PID→name lookup, avoiding per-window GetProcessById kernel round-trips.
    /// </summary>
    private static List<WindowInfo> EnumerateWindowInfos()
    {
        // Build PID → process name dictionary in one batch call
        var pidSw = Stopwatch.StartNew();
        var pidToName = new Dictionary<uint, string>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try { pidToName[(uint)proc.Id] = proc.ProcessName; }
                catch { }
            }
        }
        catch { }
        pidSw.Stop();

        var result = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var title = GetWindowText(hwnd);
            var className = GetWindowClassName(hwnd);
            GetWindowThreadProcessId(hwnd, out var pid);
            var processName = "";
            if (pid > 0)
                pidToName.TryGetValue(pid, out processName);
            result.Add(new WindowInfo(hwnd, title, className, processName ?? ""));
            return true;
        }, IntPtr.Zero);

        Logger.Info($"[EnumWindows] windows={result.Count} pidLookupBuild={pidSw.ElapsedMilliseconds}ms");
        return result;
    }

    private ConfigurationWindow? _configurationWindow;

    private void ShowConfigurationWindow()
    {
        if (_configurationWindow == null)
        {
            _configurationWindow = new ConfigurationWindow();
            _configurationWindow.LoadConfiguration(_configuration);
            _configurationWindow.Closed += (_, _) =>
            {
                _configurationWindow.SaveConfiguration(_configuration);
                ConfigurationService.Save(_configuration);
                RebuildTrayMenu();
                _configurationWindow = null;
            };
        }

        _configurationWindow.Show();
        _configurationWindow.WindowState = WindowState.Normal;
        _configurationWindow.Activate();
    }

    private void ShutdownApplication()
    {
        _desktopMonitor?.Stop();
        _desktopMonitor?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desktopMonitor?.Stop();
        _desktopMonitor?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private static ImageSource CreateImageSourceFromIcon(Icon icon)
    {
        var imageSource = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        return imageSource;
    }

    #region P/Invoke

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    private static string GetWindowText(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    #endregion
}

/// <summary>
/// Minimal ICommand implementation.
/// </summary>
internal class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
