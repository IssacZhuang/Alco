using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Alco.Profiler;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CaptureOverlay.PrepareInput();
        AppBuilder.Configure<ProfilerApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class ProfilerApp : Application
{
    /// <inheritdoc />
    public override void Initialize() => ImGuiTheme.Apply(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateLauncher(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static LaunchWindow CreateLauncher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var launcher = new LaunchWindow();
        launcher.ApplicationLaunched += (target, name) =>
        {
            var analysis = new MainWindow(target, name);
            analysis.ChooseApplicationRequested += () =>
            {
                LaunchWindow next = CreateLauncher(desktop);
                desktop.MainWindow = next;
                next.Show();
                analysis.Close();
            };
            desktop.MainWindow = analysis;
            analysis.Show();
            launcher.Close();
        };
        return launcher;
    }
}
