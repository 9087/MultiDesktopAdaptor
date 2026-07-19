namespace MultiDesktopAdaptor.Models;

/// <summary>
/// A user-defined command that runs with desktop-specific variable substitution.
/// </summary>
public class CommandDefinition
{
    public string Title { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
}

/// <summary>
/// A variable whose value can differ per virtual desktop.
/// </summary>
public class VariableDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public Dictionary<Guid, string> DesktopValues { get; set; } = new();
}

/// <summary>
/// Full configuration model for persistence.
/// </summary>
public class AppConfiguration
{
    public List<WindowRule> FollowRules { get; set; } = new();
    public List<CommandDefinition> Commands { get; set; } = new();
    public List<VariableDefinition> Variables { get; set; } = new();
}
