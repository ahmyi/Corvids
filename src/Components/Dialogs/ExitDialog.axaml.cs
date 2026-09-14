using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Corvids;

public enum ExitChoice { Cancel, KeepInTray, StopAndExit }

/// <summary>Asked when the window is closed while apps are still running.</summary>
public partial class ExitDialog : Window
{
    public ExitDialog()
    {
        InitializeComponent();
    }

    public static Task<ExitChoice> ShowAsync(Window owner, string message)
    {
        var dialog = new ExitDialog();
        dialog.MessageText.Text = message;
        return dialog.ShowDialog<ExitChoice>(owner);
    }

    private void Tray_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.KeepInTray);

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.StopAndExit);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(ExitChoice.Cancel);
}
