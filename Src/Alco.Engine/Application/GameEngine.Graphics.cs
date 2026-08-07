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
    /// Creates the render pipeline of the main view. The default is a
    /// <see cref="ForwardPipeline"/>, HDR when <see cref="PluginHDR"/> is registered.
    /// Override to use a custom pipeline (e.g. <see cref="PBRDeferredRenderPipeline"/>).
    /// </summary>
    protected virtual RenderPipeline CreateMainPipeline()
    {
        bool hdr = false;
        for (int i = 0; i < _setting.Plugins.Count; i++)
        {
            if (_setting.Plugins[i] is PluginHDR)
            {
                hdr = true;
                break;
            }
        }
        return new ForwardPipeline(
            _renderingSystem,
            hdr ? _renderingSystem.PreferredHDRPass : _renderingSystem.PreferredSDRPass,
            _builtInAssets.Shader_Blit,
            _mainView.Size.X,
            _mainView.Size.Y);
    }

    public virtual IShaderCache? CreateShaderCache(GraphicsSetting setting)
    {
        if (setting.IsShaderCacheEnabled)
        {
            if (setting.ShaderCachePath == null)
            {
                Log.Warning("Shader cache is enabled but path is not set");
                return null;
            }
            Log.Info("Shader cache is enabled, path: ", setting.ShaderCachePath);
            return new ShaderCache(setting.ShaderCachePath);
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

        return GraphicsDeviceFactory.CreateWebGPUDevice(deviceDescriptor);
    }

}