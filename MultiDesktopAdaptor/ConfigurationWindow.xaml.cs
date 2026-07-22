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

    private System.Guid _selectedDesktopId;
    private readonly List<VirtualDesktop> _desktops = new();

    public ConfigurationWindow()
    {
        InitializeComponent();
        RuleListView.ItemsSource = Rules;

        // Populate desktop list
        try
        {
            _desktops.AddRange(VirtualDesktop.GetDesktops());
            foreach (var d in _desktops)
                DesktopSelector.Items.Add(new ListBoxItem { Content = d.Name, Tag = d });
            if (_desktops.Count > 0)
            {
                DesktopSelector.SelectedIndex = 0;
                _selectedDesktopId = _desktops[0].Id;
            }
        }
        catch { }
    }

    public void LoadConfiguration(AppConfiguration config)
    {
        Rules.Clear();
        foreach (var r in config.FollowRules) Rules.Add(r);

        Commands.Clear();
        foreach (var c in config.Commands) Commands.Add(c);

        RebuildCommandList();
    }

    public void SaveConfiguration(AppConfiguration config)
    {
        config.FollowRules = Rules.ToList();
        config.Commands = Commands.ToList();
    }

    private void OnDesktopSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DesktopSelector.SelectedItem is ListBoxItem item && item.Tag is VirtualDesktop desktop)
        {
            _selectedDesktopId = desktop.Id;
            RebuildCommandList();
        }
    }

    private void RebuildCommandList()
    {
        var rows = new List<CommandRow>();

        foreach (var cmd in Commands)
        {
            var hasConfig = cmd.DesktopCommands.TryGetValue(_selectedDesktopId, out var dc)
                            && !string.IsNullOrWhiteSpace(dc.CommandLine);

            rows.Add(new CommandRow
            {
                Title = string.IsNullOrWhiteSpace(cmd.Title) ? "(untitled)" : cmd.Title,
                CommandLine = hasConfig ? dc!.CommandLine : "(not configured)",
                WorkingDirectory = hasConfig ? dc!.WorkingDirectory : "",
                ShowWindowText = hasConfig && dc!.ShowWindow ? "Yes" : "",
                ParentCommand = cmd
            });
        }

        CommandListView.ItemsSource = rows;
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
            RebuildCommandList();
        }
    }

    private void OnRemoveCommandClick(object sender, RoutedEventArgs e)
    {
        if (CommandListView.SelectedItem is CommandRow row && row.ParentCommand is CommandDefinition cmd)
        {
            Commands.Remove(cmd);
            RebuildCommandList();
        }
    }

    private async void OnCommandDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CommandListView.SelectedItem is not CommandRow row || row.ParentCommand is not CommandDefinition cmd)
            return;

        var index = Commands.IndexOf(cmd);
        if (index < 0) return;

        var dialog = new CommandDialog();
        dialog.LoadExisting(cmd);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Result != null)
        {
            Commands[index] = dialog.Result;
            RebuildCommandList();
        }
    }
}

public class CommandRow
{
    public string Title { get; set; } = "";
    public string CommandLine { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ShowWindowText { get; set; } = "";
    public CommandDefinition? ParentCommand { get; set; }
}