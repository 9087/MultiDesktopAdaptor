using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using ModernWpf.Controls;
using MultiDesktopAdaptor.Models;
using WindowsDesktop;

namespace MultiDesktopAdaptor;

public partial class VariableDialog : ContentDialog
{
    public VariableDefinition? Result { get; private set; }

    private readonly ObservableCollection<DesktopValueRow> _desktopRows = new();

    public VariableDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
        DesktopValuesList.ItemsSource = _desktopRows;

        // Populate desktop list
        try
        {
            foreach (var desktop in VirtualDesktop.GetDesktops())
            {
                _desktopRows.Add(new DesktopValueRow
                {
                    DesktopId = desktop.Id,
                    DesktopName = desktop.Name,
                    Value = ""
                });
            }
        }
        catch { }
    }

    public void LoadExisting(VariableDefinition variable)
    {
        Title = "Edit Variable";
        PrimaryButtonText = "Save";
        NameTextBox.Text = variable.Name;
        DefaultValueTextBox.Text = variable.DefaultValue;

        // Pre-fill per-desktop values
        foreach (var row in _desktopRows)
        {
            if (variable.DesktopValues.TryGetValue(row.DesktopId, out var existing))
                row.Value = existing;
        }
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationErrorText.Text = "Variable name is required.";
            ValidationErrorText.Visibility = System.Windows.Visibility.Visible;
            args.Cancel = true;
            return;
        }

        if (name.Contains(' '))
        {
            ValidationErrorText.Text = "Variable name must not contain spaces.";
            ValidationErrorText.Visibility = System.Windows.Visibility.Visible;
            args.Cancel = true;
            return;
        }

        ValidationErrorText.Visibility = System.Windows.Visibility.Collapsed;
        var desktopValues = new Dictionary<System.Guid, string>();
        foreach (var row in _desktopRows)
        {
            if (!string.IsNullOrWhiteSpace(row.Value))
                desktopValues[row.DesktopId] = row.Value.Trim();
        }

        Result = new VariableDefinition
        {
            Name = name,
            DefaultValue = DefaultValueTextBox.Text.Trim(),
            DesktopValues = desktopValues
        };
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = null;
    }
}

/// <summary>
/// Row model for the per-desktop value list in VariableDialog.
/// </summary>
public class DesktopValueRow : INotifyPropertyChanged
{
    public System.Guid DesktopId { get; set; }
    public string DesktopName { get; set; } = "";

    private string _value = "";
    public string Value
    {
        get => _value;
        set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
