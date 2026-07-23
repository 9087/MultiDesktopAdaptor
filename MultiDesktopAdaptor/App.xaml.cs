using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

        // Periodically clean up dead tracked PIDs
        var cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        cleanupTimer.Tick += (_, _) => CleanTrackedPids();
        cleanupTimer.Start();

        RebuildTrayMenu();
    }

    private void RebuildTrayMenu(List<(string processName, string title)>? mismatched = null)
    {
        if (_trayIcon?.ContextMenu == null)
            return;

        var menu = _trayIcon.ContextMenu;
        menu.Items.Clear();

        CleanTrackedPids();

        // Current desktop with switch submenu
        try
        {
            var currentDesktop = VirtualDesktop.Current;
            var desktopMenu = new System.Windows.Controls.MenuItem
            {
                Header = $"Desktop: {currentDesktop.Name}"
            };

            foreach (var desktop in VirtualDesktop.GetDesktops())
            {
                var isCurrent = desktop.Id == currentDesktop.Id;
                var switchItem = new System.Windows.Controls.MenuItem
                {
                    Header = desktop.Name,
                    IsEnabled = !isCurrent
                };
                if (!isCurrent)
                {
                    var target = desktop; // capture for closure
                    switchItem.Click += (_, _) => target.Switch();
                }
                desktopMenu.Items.Add(switchItem);
            }

            menu.Items.Add(desktopMenu);
            menu.Items.Add(new System.Windows.Controls.Separator());
        }
        catch { }

        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Show Configuration",
            Command = new RelayCommand(ShowConfigurationWindow)
        });

        // Commands submenu — colored dots reflect actual running process state
        if (_configuration.Commands.Count > 0)
        {
            var currentDesktopId = VirtualDesktop.Current.Id;
            var commandsMenu = new System.Windows.Controls.MenuItem { Header = "Commands" };

            // Scan running processes to determine which command exes are active and on which desktop
            var runningExeDesktopIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var runningPidDesktopIds = new Dictionary<int, Guid>();
            var trackedPids = new HashSet<int>();
            lock (_trackedPids) { trackedPids = new HashSet<int>(_trackedPids); }

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out var pid32);
                if (pid32 == 0) return true;
                var pid = (int)pid32;
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    var exePath = NormalizeExePath(proc.MainModule?.FileName ?? "");
                    if (exePath.Length > 0)
                    {
                        try
                        {
                            var desktop = VirtualDesktop.FromHwnd(hwnd);
                            if (desktop != null)
                            {
                                runningExeDesktopIds[exePath] = desktop.Id;
                                runningPidDesktopIds[pid] = desktop.Id;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            foreach (var cmd in _configuration.Commands)
            {
                var hasConfigured = cmd.DesktopCommands.TryGetValue(currentDesktopId, out var currentDc)
                                    && !string.IsNullOrWhiteSpace(currentDc.CommandLine);
                var hasOtherConfigured = cmd.DesktopCommands.Any(kv =>
                    kv.Key != currentDesktopId && !string.IsNullOrWhiteSpace(kv.Value.CommandLine));

                if (!hasConfigured && !hasOtherConfigured)
                    continue;

                // Check if the process is actually running (by exe path or tracked PID)
                var runningDesktopId = Guid.Empty;

                // First attempt: match by exe path
                if (hasConfigured)
                {
                    var exePath = NormalizeExePath(ExtractExePath(currentDc!.CommandLine));
                    runningExeDesktopIds.TryGetValue(exePath, out runningDesktopId);
                }

                // Second attempt: check tracked PIDs (for BAT/scripts where exe doesn't match)
                if (runningDesktopId == Guid.Empty)
                {
                    foreach (var pid in trackedPids)
                    {
                        if (runningPidDesktopIds.TryGetValue(pid, out var desktopId))
                        {
                            runningDesktopId = desktopId;
                            break;
                        }
                    }
                }

                // If still not found, check other desktops' configs
                if (runningDesktopId == Guid.Empty && hasOtherConfigured)
                {
                    foreach (var (desktopId, dc) in cmd.DesktopCommands)
                    {
                        if (string.IsNullOrWhiteSpace(dc.CommandLine)) continue;
                        var exePath = NormalizeExePath(ExtractExePath(dc.CommandLine));
                        if (runningExeDesktopIds.TryGetValue(exePath, out runningDesktopId))
                            break;
                    }
                }

                var label = string.IsNullOrWhiteSpace(cmd.Title) ? "(untitled)" : cmd.Title;
                var isRunningOnCurrent = runningDesktopId == currentDesktopId;
                var isRunningOnOther = runningDesktopId != Guid.Empty && runningDesktopId != currentDesktopId;

                string bullet;
                System.Windows.Media.Brush dotColor;
                bool enabled;

                if (isRunningOnCurrent)
                {
                    dotColor = System.Windows.Media.Brushes.Green;
                    enabled = true;
                }
                else if (isRunningOnOther)
                {
                    dotColor = System.Windows.Media.Brushes.Red;
                    enabled = false;
                }
                else
                {
                    dotColor = System.Windows.SystemColors.MenuTextBrush;
                    enabled = hasConfigured;
                }

                var headerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                headerPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "\u25CF ",
                    Foreground = dotColor
                });
                headerPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = label,
                    Margin = new System.Windows.Thickness(6, 0, 0, 0)
                });

                var item = new System.Windows.Controls.MenuItem
                {
                    Header = headerPanel,
                    IsEnabled = enabled,
                    ToolTip = hasConfigured ? currentDc!.CommandLine : null
                };

                if (enabled)
                    item.Click += (_, _) => ExecuteCommand(cmd);

                commandsMenu.Items.Add(item);
            }

            if (commandsMenu.Items.Count > 0)
                menu.Items.Add(commandsMenu);
        }

        // Mismatched processes — display only, no kill action
        if (mismatched is { Count: > 0 })
        {
            var mismatchedMenu = new System.Windows.Controls.MenuItem { Header = "Mismatched" };
            foreach (var (processName, title) in mismatched)
            {
                mismatchedMenu.Items.Add(new System.Windows.Controls.MenuItem
                {
                    Header = $"{processName} — {title}",
                    IsEnabled = false
                });
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

    #region Command execution

    /// <summary>
    /// Executes the per-desktop command line for the current virtual desktop.
    /// The command line must be an absolute path; arguments are case-sensitive.
    /// </summary>
    public void ExecuteCommand(CommandDefinition command)
    {
        var desktopId = VirtualDesktop.Current.Id;
        if (!command.DesktopCommands.TryGetValue(desktopId, out var dc)
            || string.IsNullOrWhiteSpace(dc.CommandLine))
        {
            Logger.Info($"[ExecuteCommand] No command for desktop {desktopId}");
            return;
        }

        try
        {
            CleanTrackedPids();
            Logger.Info($"[ExecuteCommand] Launching: {dc.CommandLine}");
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{dc.CommandLine}\"",
                UseShellExecute = false,
                CreateNoWindow = !dc.ShowWindow
            };
            if (!string.IsNullOrWhiteSpace(dc.WorkingDirectory))
                startInfo.WorkingDirectory = dc.WorkingDirectory;

            var process = Process.Start(startInfo);
            if (process != null)
            {
                lock (_trackedPids)
                {
                    _trackedPids.Add(process.Id);
                }
                Logger.Info($"[ExecuteCommand] PID={process.Id} tracked");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExecuteCommand] Failed: {ex.Message}");
        }
    }

    /// <summary>PIDs of processes launched via ExecuteCommand (including cmd.exe wrappers).</summary>
    private readonly HashSet<int> _trackedPids = new();

    private void CleanTrackedPids()
    {
        var dead = new List<int>();
        foreach (var pid in _trackedPids)
        {
            try { using var p = Process.GetProcessById(pid); }
            catch { dead.Add(pid); }
        }
        foreach (var pid in dead) _trackedPids.Remove(pid);
    }

    /// <summary>
    /// Scans all visible windows and finds processes whose command line matches
    /// a command configured for a DIFFERENT desktop than where the window is.
    /// </summary>
    private List<(string processName, string title)> GetMismatchedProcesses()
    {
        var result = new List<(string, string)>();
        var currentDesktop = VirtualDesktop.Current;
        var sw = Stopwatch.StartNew();

        // Build lookup: normalized exe path → configured desktop ID
        var commandTargets = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in _configuration.Commands)
        {
            foreach (var (desktopId, dc) in cmd.DesktopCommands)
            {
                if (string.IsNullOrWhiteSpace(dc.CommandLine))
                    continue;
                var exePath = NormalizeExePath(ExtractExePath(dc.CommandLine));
                if (exePath.Length > 0)
                {
                    commandTargets[exePath] = desktopId;
                    Logger.Info($"[Mismatched] target: {exePath} → desktop {desktopId}");
                }
            }
        }

        if (commandTargets.Count == 0)
            return result;

        var checkedCount = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                var exePath = NormalizeExePath(proc.MainModule?.FileName ?? "");
                if (exePath.Length == 0) return true;

                if (commandTargets.TryGetValue(exePath, out var targetDesktopId))
                {
                    checkedCount++;
                    var winDesktop = VirtualDesktop.FromHwnd(hwnd);
                    var winDesktopId = winDesktop?.Id;
                    if (winDesktopId != targetDesktopId && winDesktopId != null)
                    {
                        var title = GetWindowText(hwnd);
                        result.Add((proc.ProcessName, title));
                        Logger.Info($"[Mismatched] HIT: {proc.ProcessName} '{title}' on desktop {winDesktopId}, should be {targetDesktopId}");
                    }
                    else
                    {
                        Logger.Info($"[Mismatched] OK: {proc.ProcessName} '{GetWindowText(hwnd)}' on correct desktop {winDesktopId}");
                    }
                }
            }
            catch (Exception ex) { Logger.Info($"[Mismatched] error for PID {pid}: {ex.Message}"); }

            return true;
        }, IntPtr.Zero);

        Logger.Info($"[Mismatched] scanned={commandTargets.Count} targets mismatched={result.Count} | {sw.ElapsedMilliseconds}ms");
        return result;
    }

    /// <summary>Extracts the executable path from a command line by finding the longest prefix that exists as a file.</summary>
    private static string ExtractExePath(string commandLine)
    {
        commandLine = commandLine.Trim();
        if (commandLine.StartsWith('"'))
        {
            var end = commandLine.IndexOf('"', 1);
            return end > 0 ? commandLine[1..end] : commandLine[1..];
        }

        // Try progressively longer prefixes to handle paths with spaces (e.g. C:\Program Files\...)
        var parts = commandLine.Split(' ');
        for (int i = parts.Length; i >= 1; i--)
        {
            var candidate = string.Join(" ", parts.Take(i));
            if (File.Exists(candidate))
                return candidate;
        }

        // Fallback: return first token
        var space = commandLine.IndexOf(' ');
        return space > 0 ? commandLine[..space] : commandLine;
    }

    private static string NormalizeExePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return path; }
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
