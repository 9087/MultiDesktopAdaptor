namespace MultiDesktopAdaptor.Models;

/// <summary>
/// A rule for auto-moving windows when switching virtual desktops.
/// Any null/empty field means "match anything" for that dimension.
/// </summary>
public class WindowRule
{
    public string? WindowTitle { get; set; }
    public string? WindowClassName { get; set; }
    public string? ProcessName { get; set; }
}
