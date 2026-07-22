using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ModernWpf.Controls;
using MultiDesktopAdaptor.Models;
using WindowsDesktop;

namespace MultiDesktopAdaptor;

public partial class ConfigurationWindow : Window
{
    public ObservableCollection<WindowRule> Rules { get; } = new();
    public ObservableCollection<CommandDefinition> Commands { get; } = new();

    private readonly List<VirtualDesktop> _desktops = new();

    public ConfigurationWindow()
    {
        InitializeComponent();
        RuleListView.ItemsSource = Rules;

        try
        {
            _desktops.AddRange(VirtualDesktop.GetDesktops());
        }
        catch { }
    }

    public void LoadConfiguration(AppConfiguration config)
    {
        Rules.Clear();
        foreach (var r in config.FollowRules) Rules.Add(r);

        Commands.Clear();
        foreach (var c in config.Commands) Commands.Add(c);

        RebuildCommandSelector();
    }

    public void SaveConfiguration(AppConfiguration config)
    {
        config.FollowRules = Rules.ToList();
        config.Commands = Commands.ToList();
    }

    private void RebuildCommandSelector()
    {
        CommandSelector.Items.Clear();
        foreach (var cmd in Commands)
        {
            var label = string.IsNullOrWhiteSpace(cmd.Title) ? "(untitled)" : cmd.Title;
            CommandSelector.Items.Add(new ListBoxItem { Content = label, Tag = cmd });
        }
        if (CommandSelector.Items.Count > 0)
            CommandSelector.SelectedIndex = 0;
        else
            DesktopDetailView.ItemsSource = null;
    }

    private void OnCommandSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandSelector.SelectedItem is not ListBoxItem item || item.Tag is not CommandDefinition cmd)
        {
            DesktopDetailView.ItemsSource = null;
            return;
        }
        ShowCommandDetail(cmd);
    }

    private void ShowCommandDetail(CommandDefinition cmd)
    {
        var rows = new List<DesktopDetailRow>();
        foreach (var desktop in _desktops)
        {
            var hasConfig = cmd.DesktopCommands.TryGetValue(desktop.Id, out var dc)
                            && !string.IsNullOrWhiteSpace(dc.CommandLine);
            rows.Add(new DesktopDetailRow
            {
                DesktopName = desktop.Name,
                CommandLine = hasConfig ? dc!.CommandLine : "",
                WorkingDirectory = hasConfig ? dc!.WorkingDirectory : "",
                ShowWindowText = hasConfig && dc!.ShowWindow ? "Yes" : ""
            });
        }
        DesktopDetailView.ItemsSource = rows;
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
        {
            Commands.Add(dialog.Result);
            RebuildCommandSelector();
        }
    }

    private void OnEditCommandClick(object sender, RoutedEventArgs e)
    {
        if (CommandSelector.SelectedItem is ListBoxItem item && item.Tag is CommandDefinition cmd)
        {
            _ = EditCommandAsync(cmd);
        }
    }

    private async System.Threading.Tasks.Task EditCommandAsync(CommandDefinition cmd)
    {
        var index = Commands.IndexOf(cmd);
        if (index < 0) return;

        var dialog = new CommandDialog();
        dialog.LoadExisting(cmd);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
        {
            Commands[index] = dialog.Result;
            RebuildCommandSelector();
        }
    }

    private void OnRemoveCommandClick(object sender, RoutedEventArgs e)
    {
        if (CommandSelector.SelectedItem is ListBoxItem item && item.Tag is CommandDefinition cmd)
        {
            Commands.Remove(cmd);
            RebuildCommandSelector();
        }
    }
}

public class DesktopDetailRow
{
    public string DesktopName { get; set; } = "";
    public string CommandLine { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ShowWindowText { get; set; } = "";
}