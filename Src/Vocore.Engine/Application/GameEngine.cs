using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;
using System.Text;
using Vocore.Audio;


namespace Vocore.Engine;

/// <summary>
/// The entry point for the game <br/>
/// The integration of the game loop, base API, sdl window and graphics device
/// </summary>
public partial class GameEngine :
IDisposable
{
// #pragma warning disable CS8618
//     public static GameEngine Instance { get; private set; }
// #pragma warning restore CS8618
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
    private readonly Window _mainWindow;
    private readonly WindowRenderTarget _mainRenderTarget;

    #endregion


    #region  Internal Controllers
    private EngineProfiler _profiler;

    #endregion


    #region  State
    private int _engineThread;
    private bool _isDisposed = false;


    #endregion


    #region Properties

    /// <summary>
    /// The main thread of the game main loop
    /// </summary>
    public int MainThread
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _engineThread;
    }

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
    /// The window singleton of the game
    /// </summary>
    public Window MainWindow
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainWindow;
    }

    public WindowRenderTarget MainRenderTarget
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

    #endregion

    public GameEngine(GameEngineSetting setting)
    {
        _setting = setting;
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


        _audioDevice = AudioDeviceFactory.CreateOpenALDevice(this);
        _platform = _setting.Platform ?? new Sdl3Platform();
        _rendering = new RenderingSystem(
            _graphicsDevice,
            _renderScheduler,
            _setting.Graphics.PreferredSDRFormat,
            _setting.Graphics.PreferredHDRFormat,
            _setting.Graphics.PreferredDepthStencilFormat
            );
        
        _builtInAssets = new BuiltInAssets(_assets);

        _assets.AddFileSource(new DirectoryFileSource(setting.Assets.AssetsPath));
        InitializeDefaultAssetLoader(setting);

        //main window
        _mainWindow = CreateWindow(_setting.Window);
        _mainRenderTarget = CreateWindowRenderTarget(_mainWindow, _rendering.PrefferedSDRPass, _builtInAssets.Shader_Blit);
        AddSystem(_mainRenderTarget);

        _input = _platform.Input;

        _profiler = new EngineProfiler(this);

        InitializePlugins(_setting.Plugins);
    }



    ~GameEngine()
    {
        Dispose();
    }

    #region  Lifecycle

    /// <summary>
    /// Start the main loop of the game
    /// </summary>
    [STAThread]
    public void Run()
    {
        _engineThread = Environment.CurrentManagedThreadId;
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
        try
        {
            OnBeginFrame();
        }
        catch (Exception e)
        {
            Log.Error("[Begin Frame Error]", e);
            TryErrorStop();
        }

        OnSystemBeginFrame();

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

        OnSystemEndFrame();

        _renderScheduler.OnBeginFrame();
        EventOnEndFrame?.Invoke();

    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalStart()
    {
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
        if (_isDisposed) return;
        _isDisposed = true;
// #pragma warning disable CS8625
//         Instance = null;
// #pragma warning restore CS8625
        OnSystemDispose();
        DisposePlugins(_setting.Plugins);
        _renderScheduler.Dispose();
        MainWindow.Close();
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

    private void OnSystemBeginFrame()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnBeginFrame();
            }
            catch (Exception e)
            {
                Log.Error($"Error when pre swap frame system {_systems[i].GetType().Name}: ");
                Log.Error(e);
                TryErrorStop();
            }
        }
    }

    private void OnSystemEndFrame()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnEndFrame();
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
    /// Check if the target thread is the main thread
    /// </summary>
    /// <param name="threadId">The target thread id</param>
    /// <returns>True if the target thread is the main thread</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsMainThread(int threadId)
    {
        return threadId == _engineThread;
    }

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
