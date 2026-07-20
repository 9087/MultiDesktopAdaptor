namespace MultiDesktopAdaptor.Models;

/// <summary>
/// A user-defined command whose command line can differ per virtual desktop.
/// The command line must be an absolute path; arguments after the path are case-sensitive.
/// </summary>
public class CommandDefinition
{
    public string Title { get; set; } = string.Empty;
    public Dictionary<Guid, string> DesktopCommands { get; set; } = new();
}

/// <summary>
/// Full configuration model for persistence.
/// </summary>
public class AppConfiguration
{
    public List<WindowRule> FollowRules { get; set; } = new();
    public List<CommandDefinition> Commands { get; set; } = new();
}
