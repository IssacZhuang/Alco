using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;
using System.Text;
using Alco.Audio;


namespace Alco.Engine;

/// <summary>
/// The entry point for the game <br/>
/// The integration of the game loop, base API, view and graphics device
/// </summary>
public partial class GameEngine :
IDisposable
{
    private readonly GameEngineSetting _setting;

    #region  Resources
    private readonly GPUDevice _graphicsDevice;
    private readonly AudioDevice _audioDevice;

    private readonly BuiltInAssets _builtInAssets;
    private readonly AssetSystem _assets;

    private readonly RenderingSystem _rendering;
    private readonly IRenderScheduler _renderScheduler;
    private readonly PriorityList<IEngineSystem> _systems = new PriorityList<IEngineSystem>((x, y) => x.Order.CompareTo(y.Order));


    private readonly Platform _platform;
    private readonly InputSystem _input;
    private readonly View _mainView;
    private readonly ViewRenderTarget _mainRenderTarget;

    #endregion


    #region  Internal Controllers
    private EngineProfiler _profiler;
    private readonly GameSynchronizationContext _synchronizationContext;

    #endregion


    #region  State
    private volatile uint _disposed;


    #endregion


    #region Properties


    /// <summary>
    /// The directory of the game executable
    /// </summary>
    public static string WorkingDirectory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AppDomain.CurrentDomain.BaseDirectory;
    }

    /// <summary>
    /// The graphics device of the game<br/>
    /// Which provides the low-level graphics API,<br/>
    /// It is dangerous to use if you not familiar with graphics programming
    /// </summary>
    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _graphicsDevice;
    }

    /// <summary>
    /// The audio device of the game
    /// </summary>
    /// <value></value>
    public AudioDevice AudioDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _audioDevice;
    }

    /// <summary>
    /// The main view singleton of the game
    /// </summary>
    public View MainView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainView;
    }

    public ViewRenderTarget MainRenderTarget
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainRenderTarget;
    }

    public GPUFrameBuffer MainFrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainRenderTarget.RenderTexture.FrameBuffer;
    }


    /// <summary>
    /// The asset manager of the game<br/>
    /// Which provides the asset loading and caching
    /// </summary>
    public AssetSystem Assets
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _assets;
    }

    public BuiltInAssets BuiltInAssets
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _builtInAssets;
    }

    /// <summary>
    /// The input singleton of the game
    /// </summary>
    public InputSystem Input
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input;
    }

    /// <summary>
    /// The high-level graphics API of the game<br/>
    /// </summary>
    public RenderingSystem Rendering
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _rendering;
    }

    /// <summary>
    /// The frame updated in a second
    /// </summary>
    /// <value></value>
    public int FrameRate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _profiler.FPS;
    }

    /// <summary>
    /// The total time of CPU and GPU used in a frame
    /// </summary>
    /// <value></value>
    public float FrameTime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _profiler.FrameTime;
    }

    /// <summary>
    /// Check if the engine is disposed
    /// </summary>
    public bool IsDisposed => _disposed != 0;

    #endregion

    public GameEngine(GameEngineSetting setting)
    {
        _setting = setting;
        _synchronizationContext = new GameSynchronizationContext();
        _assets = new AssetSystem(this, _setting.Assets.LoaderThreadCount, _setting.Assets.IsProfilingEnabled);

        if (setting.Graphics.DefferedRenderSchedule)
        {
            _graphicsDevice = CreateGraphicsDevice(_setting.Graphics, 1);
            _renderScheduler = new DefferedRenderScheduler(_graphicsDevice);
        }
        else
        {
            _graphicsDevice = CreateGraphicsDevice(_setting.Graphics, 0);
            _renderScheduler = new ImmediateRenderScheduler(_graphicsDevice);
        }

        _rendering = new RenderingSystem(
            this,
            _graphicsDevice,
            _renderScheduler,
            _setting.Graphics.PreferredSDRFormat,
            _setting.Graphics.PreferredHDRFormat,
            _setting.Graphics.PreferredDepthStencilFormat
            );

        _builtInAssets = new BuiltInAssets(_assets);

        _assets.AddFileSource(new DirectoryFileSource(setting.Assets.AssetsPath));
        InitializeDefaultAssetLoader(setting);

        Task<Shader> shaderBlit = _assets.LoadAsync<Shader>(BuiltInAssetsPath.Shader_Blit);

        _platform = _setting.Platform ?? new Sdl3Platform();
        _input = _platform.Input;

        _profiler = new EngineProfiler(this);

        _audioDevice = AudioDeviceFactory.CreateOpenALDevice(this);

        //main view
        _mainView = CreateView(_setting.View);
        _mainRenderTarget = CreateViewRenderTarget(_mainView, _rendering.PrefferedSDRPass, shaderBlit.Result);
        AddSystem(_mainRenderTarget);


        InitializePlugins(_setting.Plugins);
    }


    #region  Lifecycle

    /// <summary>
    /// Start the main loop of the game
    /// </summary>
    [STAThread]
    public void Run()
    {
        InternaleRun();
    }


    /// <summary>
    /// The loop with graphics, which is used for the client
    /// </summary>
    private void InternaleRun()
    {
        _platform.OnTick += InternalTick;
        _platform.OnUpdate += InternalUpdate;
        InternalStart();
        _platform.RunMainLoop(_setting.RunOnce);
        InternalStop();
    }

    /// <summary>
    /// The start point of the game, which is called before the main loop
    /// </summary>
    protected virtual void OnStart()
    {

    }


    /// <summary>
    /// The game tick, which handles the game logic
    /// </summary>
    /// <param name="delta">The time since last tick</param>
    protected virtual void OnTick(float delta)
    {

    }


    /// <summary>
    /// The frame tick, which handles the frame logic and rendering
    /// </summary>
    /// <param name="delta">The time since last frame</param>
    protected virtual void OnUpdate(float delta)
    {

    }

    protected virtual void OnBeginFrame()
    {

    }

    /// <summary>
    /// Called before the frame swap buffer. This method is usually used for handle the custom swap chain
    /// </summary>
    protected virtual void OnEndFrame()
    {

    }


    /// <summary>
    /// Called when player exit the game
    /// </summary>
    protected virtual void OnStop()
    {

    }

    private void InternalTick(float delta)
    {
        OnSystemTick(delta);
        try
        {
            OnTick(delta);
        }
        catch (Exception e)
        {
            Log.Error("[Tick Error]", e);
            TryErrorStop();
        }
        OnSystemPostTick(delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalUpdate(float delta)
    {
        // Process any callbacks queued for the main thread
        _synchronizationContext.ProcessCallbacks();

        try
        {
            OnBeginFrame();
        }
        catch (Exception e)
        {
            Log.Error("[Begin Frame Error]", e);
            TryErrorStop();
        }
        OnSystemBeginFrame(delta);

        EventOnUpdate?.Invoke(delta);
        _renderScheduler.OnEndFrame();

        OnSystemUpdate(delta);

        try
        {
            OnUpdate(delta);
        }
        catch (Exception e)
        {
            Log.Error("[Update Error]", e);
            TryErrorStop();
        }

        OnSystemPostUpdate(delta);

        EventOnHandleAssetLoaded?.Invoke();
        _profiler.Update(delta);

        try
        {
            OnEndFrame();
        }
        catch (Exception e)
        {
            Log.Error("[End Frame Error]", e);
            TryErrorStop();
        }

        OnSystemEndFrame(delta);

        _renderScheduler.OnBeginFrame();
        EventOnEndFrame?.Invoke();

    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalStart()
    {
        // Install the game's synchronization context on the main thread
        _synchronizationContext.Install();

        try
        {
            OnStart();
        }
        catch (Exception e)
        {
            Log.Error("[Start Error]", e);
            TryErrorStop();
        }

        OnSystemStart();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalStop()
    {
        OnSystemStop();

        try
        {
            OnStop();
        }
        catch (Exception e)
        {
            Log.Error("[Stop Error]", e);
        }
    }

    private void TryErrorStop()
    {
        if (_setting.StopWhenError)
        {
            Stop();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        OnSystemDispose();
        DisposePlugins(_setting.Plugins);
        _renderScheduler.Dispose();
        MainView.Close();
        _platform.Dispose();

        EventOnDispose?.Invoke();
        GC.SuppressFinalize(this);
    }

    private void InitializePlugins(IReadOnlyList<IEnginePlugin> plugins)
    {
        for (int i = 0; i < plugins.Count; i++)
        {
            try
            {
                plugins[i].OnPostInitialize(this);
            }
            catch (Exception e)
            {
                Log.Error($"Error when initialize plugin {plugins[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void DisposePlugins(IReadOnlyList<IEnginePlugin> plugins)
    {
        for (int i = 0; i < plugins.Count; i++)
        {
            try
            {
                plugins[i].Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Error when dispose plugin {plugins[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemStart()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnStart();
            }
            catch (Exception e)
            {
                Log.Error($"Error when start system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemTick(float delta)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnTick(delta);
            }
            catch (Exception e)
            {
                Log.Error($"Error when tick system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemPostTick(float delta)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnPostTick(delta);
            }
            catch (Exception e)
            {
                Log.Error($"Error when post tick system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemUpdate(float delta)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnUpdate(delta);
            }
            catch (Exception e)
            {
                Log.Error($"Error when update system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemPostUpdate(float delta)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnPostUpdate(delta);
            }
            catch (Exception e)
            {
                Log.Error($"Error when post update system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemBeginFrame(float deltaTime)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnBeginFrame(deltaTime);
            }
            catch (Exception e)
            {
                Log.Error($"Error when pre swap frame system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemEndFrame(float deltaTime)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnEndFrame(deltaTime);
            }
            catch (Exception e)
            {
                Log.Error($"Error when pre swap frame system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }


    private void OnSystemStop()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnStop();
            }
            catch (Exception e)
            {
                Log.Error($"Error when stop system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemDispose()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Error when dispose system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }



    #endregion

    #region API

    /// <summary>
    /// Stop the game engine. This will stop the main loop and dispose all the runtime objects in the end of the frame
    /// </summary>
    public void Stop()
    {
        _platform.StopMainLoop();
    }

    public void AddSystem(IEngineSystem system)
    {
        _systems.Add(system);
    }

    public void RemoveSystem(IEngineSystem system)
    {
        _systems.Remove(system);
    }

    #endregion

}
