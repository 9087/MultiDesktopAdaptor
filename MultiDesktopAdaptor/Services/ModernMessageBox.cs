using System.Threading.Tasks;
using ModernWpf.Controls;

namespace MultiDesktopAdaptor.Services;

/// <summary>
/// Simple MessageBox-style wrapper around ModernWpf ContentDialog.
/// </summary>
public static class ModernMessageBox
{
    public static async Task<bool> ShowAsync(string text, string title,
        string primaryButtonText = "OK", string secondaryButtonText = "")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = text,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync(ContentDialogPlacement.Popup);
        return result == ContentDialogResult.Primary;
    }
}
