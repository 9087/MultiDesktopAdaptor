using ModernWpf.Controls;
using MultiDesktopAdaptor.Models;

namespace MultiDesktopAdaptor;

public partial class CommandDialog : ContentDialog
{
    public CommandDefinition? Result { get; private set; }

    public CommandDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
    }

    public void LoadExisting(CommandDefinition command)
    {
        Title = "Edit Command";
        PrimaryButtonText = "Save";
        TitleTextBox.Text = command.Title;
        CommandLineTextBox.Text = command.CommandLine;
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var commandLine = CommandLineTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            ValidationErrorText.Text = "Command line is required.";
            ValidationErrorText.Visibility = System.Windows.Visibility.Visible;
            args.Cancel = true;
            return;
        }

        ValidationErrorText.Visibility = System.Windows.Visibility.Collapsed;
        Result = new CommandDefinition
        {
            Title = TitleTextBox.Text.Trim(),
            CommandLine = commandLine
        };
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = null;
    }
}
