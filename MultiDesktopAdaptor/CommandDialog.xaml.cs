using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using ModernWpf.Controls;
using MultiDesktopAdaptor.Models;
using WindowsDesktop;

namespace MultiDesktopAdaptor;

public partial class CommandDialog : ContentDialog
{
    public CommandDefinition? Result { get; private set; }

    private readonly ObservableCollection<DesktopCommandRow> _desktopRows = new();

    public CommandDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
        DesktopCommandsList.ItemsSource = _desktopRows;

        try
        {
            foreach (var desktop in VirtualDesktop.GetDesktops())
            {
                _desktopRows.Add(new DesktopCommandRow
                {
                    DesktopId = desktop.Id,
                    DesktopName = desktop.Name
                });
            }
        }
        catch { }
    }

    public void LoadExisting(CommandDefinition command)
    {
        Title = "Edit Command";
        PrimaryButtonText = "Save";
        TitleTextBox.Text = command.Title;

        foreach (var row in _desktopRows)
        {
            if (command.DesktopCommands.TryGetValue(row.DesktopId, out var existing))
            {
                row.CommandLine = existing.CommandLine;
                row.WorkingDirectory = existing.WorkingDirectory;
                row.ShowWindow = existing.ShowWindow;
            }
        }
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var desktopCommands = new Dictionary<System.Guid, DesktopCommand>();

        foreach (var row in _desktopRows)
        {
            var cmdLine = row.CommandLine.Trim();
            if (string.IsNullOrWhiteSpace(cmdLine))
                continue;

            if (!Path.IsPathRooted(cmdLine))
            {
                ValidationErrorText.Text = $"Not an absolute path: {cmdLine}";
                ValidationErrorText.Visibility = System.Windows.Visibility.Visible;
                args.Cancel = true;
                return;
            }

            desktopCommands[row.DesktopId] = new DesktopCommand
            {
                CommandLine = cmdLine,
                WorkingDirectory = row.WorkingDirectory.Trim(),
                ShowWindow = row.ShowWindow
            };
        }

        if (desktopCommands.Count == 0)
        {
            ValidationErrorText.Text = "At least one desktop must be configured.";
            ValidationErrorText.Visibility = System.Windows.Visibility.Visible;
            args.Cancel = true;
            return;
        }

        ValidationErrorText.Visibility = System.Windows.Visibility.Collapsed;
        Result = new CommandDefinition
        {
            Title = TitleTextBox.Text.Trim(),
            DesktopCommands = desktopCommands
        };
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = null;
    }
}

public class DesktopCommandRow : INotifyPropertyChanged
{
    public System.Guid DesktopId { get; set; }
    public string DesktopName { get; set; } = "";

    private string _commandLine = "";
    public string CommandLine
    {
        get => _commandLine;
        set { _commandLine = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CommandLine))); }
    }

    private string _workingDirectory = "";
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { _workingDirectory = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkingDirectory))); }
    }

    private bool _showWindow;
    public bool ShowWindow
    {
        get => _showWindow;
        set { _showWindow = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWindow))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
