using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

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
        if (setting.SurfaceFormat is null or Format.Undefined)
        {
            setting.SurfaceFormat = _setting.Graphics.PreferredSurfaceFormat;
        }

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
    /// Creates compiler options for <see cref="RenderingSystem.Shaders"/>, binding
    /// <see cref="AssetSystem"/> module resolution and shader cache settings.
    /// </summary>
    /// <returns>The compiler options passed to the rendering system.</returns>
    protected virtual ShaderCompilerOptions CreateShaderCompilerOptions()
    {
        (ShaderFileResolver fileResolver, ShaderPathExists pathExists) = ShaderModuleResolver.CreateIndexed(
            path => _assetSystem.TryGetStream(path, out Stream? stream) ? stream : null,
            () => _assetSystem.AllAssetNames,
            () => _assetSystem.Version);

        return new ShaderCompilerOptions
        {
            FileResolver = fileResolver,
            PathExists = pathExists,
            CacheDirectory = CreateShaderCacheDirectory(_setting.Graphics),
        };
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
    /// Resolves the persistent GPU pipeline cache directory, or null when
    /// the setting disables pipeline caching.
    /// </summary>
    public virtual string? CreatePipelineCacheDirectory(GraphicsSetting setting)
    {
        if (!setting.IsPipelineCacheEnabled)
            return null;
        if (string.IsNullOrEmpty(setting.PipelineCachePath))
        {
            Log.Warning("Pipeline cache is enabled but path is not set");
            return null;
        }
        Log.Info("Pipeline cache is enabled, path: ", setting.PipelineCachePath);
        return setting.PipelineCachePath;
    }

    private GPUDevice CreateGraphicsDevice(GraphicsSetting setting, bool headless)
    {
        Gpu.SetErrorCallback(OnGpuError);

        return Gpu.CreateDevice(new DeviceDesc
        {
            Backend = setting.Backend,
            Headless = headless,
            PipelineCacheDirectory = CreatePipelineCacheDirectory(setting),
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
