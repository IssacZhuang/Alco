using System;
using Silk.NET.Windowing;
using Vocore.Graphics;

namespace Vocore.Engine{
    public class PluginRuntimeInfo : BaseEnginePlugin
    {
        public override int Priority => -1000;

        public override void OnInitilize(GameEngine engine, ref GameEngineSetting setting)
        {
            GPUDevice device = engine.GraphicsDevice;

            // Log.Info("--- Compatibility ---");
            // Log.Info("D3D11 Supported: \t" + GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D11));
            // Log.Info("Vulkan Supported: \t" + GraphicsDevice.IsBackendSupported(GraphicsBackend.Vulkan));
            // Log.Info("OpenGL Supported: \t" + GraphicsDevice.IsBackendSupported(GraphicsBackend.OpenGL));
            // Log.Info("Metal Supported: \t" + GraphicsDevice.IsBackendSupported(GraphicsBackend.Metal));
            // Log.Info("OpenGL ES Supported: \t" + GraphicsDevice.IsBackendSupported(GraphicsBackend.OpenGLES));
            // Log.Info("\n--- Device Info ---");
            // Log.Info("Graphics Backend: \t" + device.BackendType);
            // Log.Info("Graphics Version: \t\t" + device.ApiVersion);
            // Log.Info("IsDepthRangeZeroToOne: \t" + device.IsDepthRangeZeroToOne);
            // Log.Info("IsClipSpaceYInverted: \t" + device.IsClipSpaceYInverted);
            // Log.Info("IsUvOriginTopLeft: \t" + device.IsUvOriginTopLeft);
            Log.Info("\n--- Thread ---");
            Log.Info("CPU Thread Count: \t" + Environment.ProcessorCount);
            Log.Info("Main Thread Id\t" + engine.MainThread);
            
            
        }
    }
}