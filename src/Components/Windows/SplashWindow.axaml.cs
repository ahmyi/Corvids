using Avalonia.Controls;
using Avalonia.Input;

namespace Corvids;

/// <summary>
/// Borderless splash. On launch it is shown briefly and closed by App. Shown from Settings ▸ About it stays
/// until the user clicks it or presses Escape.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow() : this(aboutMode: false) { }

    public SplashWindow(bool aboutMode)
    {
        InitializeComponent();

        var version = GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        VersionLabel.Text = $"v{version}";

        if (aboutMode)
        {
            Progress.IsVisible = false;
            StatusLabel.Text = "Click anywhere to close";
            PointerPressed += (_, _) => Close();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        }
    }
}
