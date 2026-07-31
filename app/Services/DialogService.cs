using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TermServMultiScreen.Core;

namespace TermServMultiScreen.Services;

/// <summary>Boîtes de dialogue standard, toutes rattachées à la fenêtre principale.</summary>
public static class DialogService
{
    private static bool _busy;

    public static async Task InfoAsync(string title, string message)
    {
        await ShowAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Fermer",
            DefaultButton = ContentDialogButton.Close
        });
    }

    public static async Task<bool> ConfirmAsync(string title, string message,
        string primary = "Continuer", string cancel = "Annuler")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Primary
        };
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public static async Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial, SelectionStart = initial.Length, PlaceholderText = label };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Valider",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);

        return await ShowAsync(dialog) == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    /// <summary>Affiche un texte technique (aperçu .rdp, diagnostic) avec bouton de copie.</summary>
    public static async Task ShowTextAsync(string title, string text)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12.5,
            Height = 460,
            MinWidth = 780,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = "Copier",
            CloseButtonText = "Fermer",
            DefaultButton = ContentDialogButton.Close
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;   // garde la fenêtre ouverte après la copie
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            catch (Exception ex) { Log.Write($"Copie impossible : {ex.Message}"); }
        };

        await ShowAsync(dialog);
    }

    private static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var root = AppServices.XamlRoot;
        if (root is null || _busy) return ContentDialogResult.None;

        dialog.XamlRoot = root;
        if (AppServices.MainWindow?.Content is FrameworkElement element)
            dialog.RequestedTheme = element.ActualTheme;

        _busy = true;
        try { return await dialog.ShowAsync(); }
        catch (Exception ex)
        {
            Log.Write($"Dialogue « {dialog.Title} » : {ex.Message}");
            return ContentDialogResult.None;
        }
        finally { _busy = false; }
    }
}
