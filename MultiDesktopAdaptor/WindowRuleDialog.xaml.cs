using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using ModernWpf.Controls;
using MultiDesktopAdaptor.Models;

namespace MultiDesktopAdaptor;

public partial class WindowRuleDialog : ContentDialog
{
    public WindowRule? Result { get; private set; }

    public WindowRuleDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
        Loaded += (_, _) => PopulateWindows();

        // Clear error text when user edits any field
        WindowTitleComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler(OnRuleFieldChanged));
        WindowClassComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler(OnRuleFieldChanged));
        ProcessNameComboBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler(OnRuleFieldChanged));
    }

    /// <summary>
    /// Pre-fills the dialog for editing an existing rule.
    /// </summary>
    public void LoadExisting(WindowRule rule)
    {
        Title = "Edit Window Rule";
        PrimaryButtonText = "Save";
        WindowTitleComboBox.Text = rule.WindowTitle ?? "";
        WindowClassComboBox.Text = rule.WindowClassName ?? "";
        ProcessNameComboBox.Text = rule.ProcessName ?? "";
    }

    private void PopulateWindows()
    {
        var titles = new HashSet<string>();
        var classes = new HashSet<string>();
        var processes = new HashSet<string>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            var title = GetWindowText(hwnd);
            if (!string.IsNullOrWhiteSpace(title))
                titles.Add(title);

            var className = GetClassName(hwnd);
            if (!string.IsNullOrWhiteSpace(className))
                classes.Add(className);

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid > 0)
            {
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    processes.Add(proc.ProcessName);
                }
                catch { }
            }

            return true;
        }, IntPtr.Zero);

        WindowTitleComboBox.ItemsSource = titles.OrderBy(x => x);
        WindowClassComboBox.ItemsSource = classes.OrderBy(x => x);
        ProcessNameComboBox.ItemsSource = processes.OrderBy(x => x);
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var title = NullIfEmpty(WindowTitleComboBox.Text);
        var className = NullIfEmpty(WindowClassComboBox.Text);
        var processName = NullIfEmpty(ProcessNameComboBox.Text);

        if (title == null && className == null && processName == null)
        {
            StatusText.Text = "At least one field is required.";
            StatusText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;
        Result = new WindowRule
        {
            WindowTitle = title,
            WindowClassName = className,
            ProcessName = processName
        };
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = null;
    }

    private static string? NullIfEmpty(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void OnFlashMatchesClick(object sender, RoutedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;
        var rule = new WindowRule
        {
            WindowTitle = NullIfEmpty(WindowTitleComboBox.Text),
            WindowClassName = NullIfEmpty(WindowClassComboBox.Text),
            ProcessName = NullIfEmpty(ProcessNameComboBox.Text)
        };

        if (rule.WindowTitle == null && rule.WindowClassName == null && rule.ProcessName == null)
        {
            StatusText.Text = "Fill at least one field to test.";
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        var matchedCount = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            var title = GetWindowText(hwnd);
            var className = GetClassName(hwnd);
            GetWindowThreadProcessId(hwnd, out var pid);
            var processName = "";
            if (pid > 0)
            {
                try { processName = Process.GetProcessById((int)pid).ProcessName; }
                catch { }
            }

            if (MatchesRule(title, className, processName, rule))
            {
                matchedCount++;
                FlashWindow(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        if (matchedCount == 0)
        {
            StatusText.Text = "No visible windows matched.";
            StatusText.Visibility = Visibility.Visible;
        }
        else
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRuleFieldChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;
    }

    private static bool MatchesRule(string title, string className, string processName, WindowRule rule)
    {
        if (rule.WindowTitle != null)
        {
            if (!title.Contains(rule.WindowTitle, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (rule.WindowClassName != null)
        {
            if (!className.Equals(rule.WindowClassName, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (rule.ProcessName != null)
        {
            if (!processName.Equals(rule.ProcessName, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static void FlashWindow(IntPtr hwnd)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
            uCount = 3,
            dwTimeout = 0
        };
        FlashWindowEx(ref info);
    }

    #region P/Invoke

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private const uint FLASHW_TRAY = 0x00000002;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private static string GetWindowText(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    #endregion
}
