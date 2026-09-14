using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Corvids;

public partial class App : Application
{
    private static readonly TimeSpan SplashDuration = TimeSpan.FromMilliseconds(1600);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShowSplashThenMain(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows the splash, then creates and shows the main window, then dismisses the splash.</summary>
    private async void ShowSplashThenMain(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splash = new SplashWindow();
        splash.Show();
        await Task.Delay(SplashDuration);

        var main = new MainWindow();
        desktop.MainWindow = main;
        main.Show(); // MainWindow.Opened auto-starts apps and hides itself if "start minimized" is set
        splash.Close();
    }

    private MainWindow? Main =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

    /// <summary>Updates the tray tooltip, e.g. "Corvids - 2 of 5 running".</summary>
    public static void SetTrayToolTip(string text)
    {
        if (Current is not App app) return;
        var icons = TrayIcon.GetIcons(app);
        if (icons is { Count: > 0 }) icons[0].ToolTipText = text;
    }

    private void Tray_Clicked(object? sender, EventArgs e) => Main?.ShowFromTray();

    private void TrayShow_Click(object? sender, EventArgs e) => Main?.ShowFromTray();

    private void TrayExit_Click(object? sender, EventArgs e) => Main?.StopAllAndExit();
}
