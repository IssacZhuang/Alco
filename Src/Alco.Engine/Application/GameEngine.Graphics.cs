using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.Engine;

/// <summary>
/// View and graphics device creation for the engine.
/// </summary>
public partial class GameEngine
{
    /// <summary>
    /// Creates a view using the platform backend.
    /// </summary>
    /// <param name="setting">The view configuration.</param>
    /// <returns>The created view.</returns>
    public View CreateView(ViewSetting setting)
    {
        return _platform.CreateView(_graphicsDevice, setting);
    }

    /// <summary>
    /// Creates a presenter for an additional view.
    /// </summary>
    /// <param name="view">The view to present.</param>
    /// <returns>The created presenter.</returns>
    public ViewPresenter CreateViewPresenter(View view)
    {
        return new ViewPresenter(view, _renderingSystem);
    }

    /// <summary>
    /// Resolves the slang module system's on-disk cache directory, or null when
    /// the setting disables shader caching.
    /// </summary>
    public virtual string? CreateShaderCacheDirectory(GraphicsSetting setting)
    {
        if (setting.IsShaderCacheEnabled)
        {
            if (setting.ShaderCachePath == null)
            {
                Log.Warning("Shader cache is enabled but path is not set");
                return null;
            }
            Log.Info("Shader cache is enabled, path: ", setting.ShaderCachePath);
            return setting.ShaderCachePath;
        }
        return null;
    }

    /// <summary>
    /// Resolves the font atlas on-disk cache directory, or null when
    /// the setting disables font caching.
    /// </summary>
    public virtual string? CreateFontCacheDirectory(GraphicsSetting setting)
    {
        if (setting.IsFontCacheEnabled)
        {
            if (setting.FontCachePath == null)
            {
                Log.Warning("Font cache is enabled but path is not set");
                return null;
            }
            Log.Info("Font cache is enabled, path: ", setting.FontCachePath);
            return setting.FontCachePath;
        }
        return null;
    }

    private static GPUDevice CreateGraphicsDevice(GraphicsSetting setting, bool headless)
    {
        Gpu.SetErrorCallback(OnGpuError);

        return Gpu.CreateDevice(new DeviceDesc
        {
            Backend = setting.Backend,
            Headless = headless,
        });
    }

    private static void OnGpuError(string message, bool fatal)
    {
        if (fatal)
        {
            Log.Error("[GPU Fatal] ", message);
            return;
        }

        Log.Error("[GPU Error] ", message);
    }
}
