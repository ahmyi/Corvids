using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Corvids;

/// <summary>Minimal yes/no dialog, since Avalonia ships no MessageBox.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static Task<bool> ShowAsync(Window owner, string title, string message, string confirmLabel)
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmLabel;
        return dialog.ShowDialog<bool>(owner);
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
