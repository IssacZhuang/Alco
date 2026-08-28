using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public partial class GameEngine
{
    public View CreateView(ViewSetting setting)
    {
        return _platform.CreateView(_graphicsDevice, setting);
    }

    /// <summary>
    /// Creates a presenter for an additional view. The caller drives it manually
    /// (<see cref="ViewPresenter.BeginFrame"/>/<see cref="ViewPresenter.EndFrame"/>) and pairs
    /// it with a <see cref="RenderPipeline"/> that resolves into the presenter's frame buffer.
    /// </summary>
    public ViewPresenter CreateViewPresenter(View view)
    {
        return new ViewPresenter(view);
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

    private GPUDevice CreateGraphicsDevice(GraphicsSetting setting, uint disposeDelay)
    {
        if (setting.Backend == GraphicsBackend.None)
        {
            return GraphicsDeviceFactory.GetNoGPUDevice();
        }

        DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
        {
            Host = this,
            Debug = setting.DebugInfo,
            Backend = setting.Backend,
            PreferredSurfaceFormat = setting.PreferredSurfaceFormat,
            PushConstantsSize = 128,
            DisposeDelay = disposeDelay,
            Name = "graphics_device"
        };

        if (setting.Backend == GraphicsBackend.NativeVulkan)
        {
            return GraphicsDeviceFactory.CreateVulkanDevice(deviceDescriptor);
        }

        // every other backend runs through wgpu; the wgpu device maps the
        // requested backend onto its instance/adapter options
        return GraphicsDeviceFactory.CreateWebGPUDevice(deviceDescriptor);
    }

}