using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace Alco.Profiler;

/// <summary>Displays capture controls above the target without owning a second recording session.</summary>
internal sealed partial class CaptureOverlay : Window
{
    private const uint WsExNoActivate = 0x08000000;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmPointerActivate = 0x024B;
    private const int NoActivate = 3;

    private readonly Func<Task> _start;
    private readonly Func<Task> _stop;

    /// <summary>Creates the compact capture window with actions supplied by the analysis session.</summary>
    public CaptureOverlay(string targetName, Func<Task> start, Func<Task> stop)
    {
        _start = start;
        _stop = stop;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWindowStylesCallback(this, static (style, extended) => (style, extended | WsExNoActivate));
            Win32Properties.AddWndProcHookCallback(this, HandleActivation);
        }
        InitializeComponent();
        OverlayTarget.Text = targetName;
    }

    /// <summary>Gets whether the platform can show the overlay, with non-activating mouse input on Windows.</summary>
    public static bool IsSupported => !OperatingSystem.IsWindows()
        || (OperatingSystem.IsWindowsVersionAtLeast(6, 2) && IsMouseInPointerEnabled() != 0);

    /// <summary>Enables pointer messages before the profiler creates any Windows handles.</summary>
    public static void PrepareInput()
    {
        // Avalonia's legacy Win32 mouse path calls SetFocus even on a no-activate window.
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 2) && IsMouseInPointerEnabled() == 0)
        {
            EnableMouseInPointer(1);
        }
    }

    /// <summary>Places the overlay near the upper-right corner of the screen's work area.</summary>
    public void PlaceOn(Screen screen)
    {
        PixelRect area = screen.WorkingArea;
        int margin = (int)Math.Round(16 * screen.Scaling);
        int width = (int)Math.Round(Width * screen.Scaling);
        Position = new PixelPoint(Math.Max(area.X, area.Right - width - margin), area.Y + margin);
    }

    /// <summary>Mirrors the current capture availability and status from the analysis session.</summary>
    public void UpdateCaptureControls(bool canStart, bool canStop, string status, string? detail)
    {
        OverlayStartButton.IsEnabled = canStart;
        OverlayStopButton.IsEnabled = canStop;
        CaptureStatus.Text = status;
        ToolTip.SetTip(CaptureStatus, detail);
    }

    private async void StartClicked(object? sender, RoutedEventArgs e) => await _start();

    private async void StopClicked(object? sender, RoutedEventArgs e) => await _stop();

    private void DismissClicked(object? sender, RoutedEventArgs e) => Close();

    private static nint HandleActivation(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is not (WmMouseActivate or WmPointerActivate))
        {
            return 0;
        }

        handled = true;
        return NoActivate;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int IsMouseInPointerEnabled();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int EnableMouseInPointer(int enabled);
}
