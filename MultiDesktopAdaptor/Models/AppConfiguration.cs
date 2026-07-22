namespace MultiDesktopAdaptor.Models;

/// <summary>
/// Per-desktop command configuration: command line + working directory.
/// </summary>
public class DesktopCommand
{
    public string CommandLine { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool ShowWindow { get; set; }
}

/// <summary>
/// A user-defined command whose command line and working directory can differ per virtual desktop.
/// </summary>
public class CommandDefinition
{
    public string Title { get; set; } = string.Empty;
    public Dictionary<Guid, DesktopCommand> DesktopCommands { get; set; } = new();
}

/// <summary>
/// Full configuration model for persistence.
/// </summary>
public class AppConfiguration
{
    public List<WindowRule> FollowRules { get; set; } = new();
    public List<CommandDefinition> Commands { get; set; } = new();
}
