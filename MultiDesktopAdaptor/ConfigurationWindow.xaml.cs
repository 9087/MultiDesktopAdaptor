using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using ModernWpf.Controls;
using MultiDesktopAdaptor.Models;

namespace MultiDesktopAdaptor;

public partial class ConfigurationWindow : Window
{
    public ObservableCollection<WindowRule> Rules { get; } = new();
    public ObservableCollection<CommandDefinition> Commands { get; } = new();
    public ObservableCollection<VariableDefinition> Variables { get; } = new();

    public ConfigurationWindow()
    {
        InitializeComponent();
        RuleListView.ItemsSource = Rules;
        CommandListView.ItemsSource = Commands;
        VariableListView.ItemsSource = Variables;
    }

    public void LoadConfiguration(AppConfiguration config)
    {
        Rules.Clear();
        foreach (var r in config.FollowRules) Rules.Add(r);

        Commands.Clear();
        foreach (var c in config.Commands) Commands.Add(c);

        Variables.Clear();
        foreach (var v in config.Variables) Variables.Add(v);
    }

    public void SaveConfiguration(AppConfiguration config)
    {
        config.FollowRules = Rules.ToList();
        config.Commands = Commands.ToList();
        config.Variables = Variables.ToList();
    }

    private async void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WindowRuleDialog();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            Rules.Add(dialog.Result);
    }

    private void OnRemoveRuleClick(object sender, RoutedEventArgs e)
    {
        if (RuleListView.SelectedItem is WindowRule rule)
            Rules.Remove(rule);
    }

    private async void OnRuleDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RuleListView.SelectedItem is not WindowRule rule)
            return;

        var index = Rules.IndexOf(rule);
        var dialog = new WindowRuleDialog();
        dialog.LoadExisting(rule);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            Rules[index] = dialog.Result;
    }

    private async void OnAddCommandClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CommandDialog();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            Commands.Add(dialog.Result);
    }

    private void OnRemoveCommandClick(object sender, RoutedEventArgs e)
    {
        if (CommandListView.SelectedItem is CommandDefinition cmd)
            Commands.Remove(cmd);
    }

    private async void OnCommandDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CommandListView.SelectedItem is not CommandDefinition command)
            return;

        var index = Commands.IndexOf(command);
        var dialog = new CommandDialog();
        dialog.LoadExisting(command);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            Commands[index] = dialog.Result;
    }

    private async void OnAddVariableClick(object sender, RoutedEventArgs e)
    {
        var dialog = new VariableDialog();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            Variables.Add(dialog.Result);
    }

    private void OnRemoveVariableClick(object sender, RoutedEventArgs e)
    {
        if (VariableListView.SelectedItem is VariableDefinition variable)
            Variables.Remove(variable);
    }

    private async void OnVariableDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (VariableListView.SelectedItem is not VariableDefinition variable)
            return;

        var index = Variables.IndexOf(variable);
        var dialog = new VariableDialog();
        dialog.LoadExisting(variable);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
            Variables[index] = dialog.Result;
    }
}