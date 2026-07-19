using WindowsDesktop;

namespace MultiDesktopAdaptor.Services;

/// <summary>
/// Event args carrying desktop info after a virtual desktop switch.
/// </summary>
public class DesktopSwitchEventArgs : EventArgs
{
    public string DesktopId { get; init; } = string.Empty;
    public string DesktopName { get; init; } = string.Empty;
}

/// <summary>
/// Monitors virtual desktop switches via the <c>VirtualDesktop</c> library's
/// <c>CurrentChanged</c> event (Grabacr07). This wraps the internal
/// <c>IVirtualDesktopNotification</c> COM interface for reliable, event-driven detection.
/// </summary>
public sealed class DesktopMonitorService : IDisposable
{
    private bool _isRunning;

    public event EventHandler<DesktopSwitchEventArgs>? DesktopSwitched;

    public void Start()
    {
        if (_isRunning)
            return;

        // Force static initialization — the library lazily registers COM notification
        // objects on first access. Without this, CurrentChanged may never fire.
        _ = VirtualDesktop.GetDesktops();

        VirtualDesktop.CurrentChanged += OnCurrentChanged;
        _isRunning = true;
        Logger.Info("[DesktopMonitor] Started, subscribed to CurrentChanged.");
    }

    public void Stop()
    {
        if (!_isRunning)
            return;

        VirtualDesktop.CurrentChanged -= OnCurrentChanged;
        _isRunning = false;
    }

    public void Dispose() => Stop();

    private void OnCurrentChanged(object? sender, VirtualDesktopChangedEventArgs eventArgs)
    {
        Logger.Info($"[DesktopMonitor] CurrentChanged -> {eventArgs.NewDesktop.Name}");
        var desktop = eventArgs.NewDesktop;
        DesktopSwitched?.Invoke(this, new DesktopSwitchEventArgs
        {
            DesktopId = desktop.Id.ToString("B"),
            DesktopName = desktop.Name
        });
    }
}
