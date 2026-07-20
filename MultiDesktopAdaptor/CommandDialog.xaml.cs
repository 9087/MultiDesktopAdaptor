using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
                row.CommandLine = existing;
        }
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var desktopCommands = new Dictionary<System.Guid, string>();

        foreach (var row in _desktopRows)
        {
            var line = row.CommandLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!Path.IsPathRooted(line))
            {
                ValidationErrorText.Text = $"Not an absolute path: {line}";
                ValidationErrorText.Visibility = System.Windows.Visibility.Visible;
                args.Cancel = true;
                return;
            }

            desktopCommands[row.DesktopId] = line;
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

    public event PropertyChangedEventHandler? PropertyChanged;
}
