using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;
using System.Text;


namespace Vocore.Engine;

/// <summary>
/// The entry point for the game <br/>
/// The integration of the game loop, base API, sdl window and graphics device
/// </summary>
public partial class GameEngine : IDisposable
{
#pragma warning disable CS8618
    public static GameEngine Instance { get; private set; }
#pragma warning restore CS8618
    private readonly GameEngineSetting _setting;

    #region  Resources
    private readonly GPUDevice _graphicsDevice;
    private readonly BuiltInAssets _builtInAssets;
    private readonly AssetSystem _assets;
    private readonly InputSystem _input;
    private readonly RenderingSystem _rendering;
    private readonly PriorityList<IEngineSystem> _systems = new PriorityList<IEngineSystem>((x, y) => x.Order.CompareTo(y.Order));

    private readonly Window _mainWindow;
    private readonly WindowRenderTarget _mainRenderTarget;

    #endregion

    #region  Internal Controller
    internal EngineGraphics _graphics;
    internal EngineTimer _timer;
    internal EngineProfiler _profiler;

    #endregion


    #region  State
    private int _engineThread;
    private bool _isDisposed = false;
    private bool _isRunning = false;
    private bool _shouldResize = false;

    private uint2 _windowSize;

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
    /// Check if the current thread is the main thread
    /// </summary>
    public bool IsCalledFromMainThread
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _engineThread == Environment.CurrentManagedThreadId;
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

    public GPUSwapchain? WindowSwapchain
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainWindow.Swapchain;
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
        if (Instance != null)
        {
            throw new Exception("The GameEngine can only have one instance.");
        }
        Instance = this;
        _setting = setting;

        _graphicsDevice = CreateGraphicsDevice(_setting.Graphics);

        _rendering = new RenderingSystem(_graphicsDevice);
        _assets = new AssetSystem(_setting.Assets.LoaderThreadCount);
        _builtInAssets = new BuiltInAssets(_assets);

        _assets.AddFileSource(new DirectoryFileSource(setting.Assets.AssetsPath));
        InitializeDefaultAssetLoader();

        //main window
        _mainWindow = CreateWindow(_setting.Window);
        _mainRenderTarget = CreateWindowRenderTarget(_mainWindow, _rendering.PrefferedSDRPass, _builtInAssets.Shader_Blit);
        AddSystem(_mainRenderTarget);
        _mainWindow.OnResize += MainWindowResize;

        _input = _mainWindow.Input;

        _graphics = new EngineGraphics(this);
        _timer = new EngineTimer(this);
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
        _isRunning = true;

        Assets.SetMainThread();

        InternaleRun();
    }


    /// <summary>
    /// The loop with graphics, which is used for the client
    /// </summary>
    private void InternaleRun()
    {
        InternalStart();

        if (_setting.RunOnce)
        {
            _isRunning = true;
            InternalUpdate();
            _isRunning = false;
        }
        else
        {
            while (_isRunning)
            {
                InternalUpdate();
            }
        }

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

    /// <summary>
    /// Called when the window is resized
    /// </summary>
    /// <param name="size">The new size of the window</param>
    protected virtual void OnMainWindowResize(uint2 size)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalUpdate()
    {
        _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
        _input.DoEvent();
        _input.Update();

        if (canInvokePhysicsTick)
        {
            OnSystemTick(physicsDeltaTime);

            try
            {
                OnTick(physicsDeltaTime);
            }
            catch (Exception e)
            {
                Log.Error("[Tick Error]", e);
                TryErrorStop();
            }

            OnSystemPostTick(physicsDeltaTime);
        }

        _graphics.BeginFrameUpdate(WindowSwapchain);

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

        OnSystemUpdate(updateDeltaTime);

        try
        {
            OnUpdate(updateDeltaTime);
        }
        catch (Exception e)
        {
            Log.Error("[Update Error]", e);
            TryErrorStop();
        }

        OnSystemPostUpdate(updateDeltaTime);

        _assets.OnUpdate();
        _profiler.Update(updateDeltaTime);
        _input.Reset();//reset input state

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
        _graphics.EndFrame();//swap buffer

        if (_shouldResize)
        {
            OnMainWindowResize(_windowSize);
            OnSystemMainWindowResize(_windowSize);
            _shouldResize = false;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalStart()
    {
        _timer.Start();
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
#pragma warning disable CS8625
        Instance = null;
#pragma warning restore CS8625
        OnSystemDispose();
        DisposePlugins(_setting.Plugins);
        GraphicsDevice.Dispose();
        MainWindow.Close();
        Assets.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InitializeDefaultAssetLoader()
    {
        Assets.RegisterAssetLoader(new AssetLoaderFontTTF(Rendering));
        Assets.RegisterAssetLoader(new AssetLoaderTexture2D(Rendering));
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSLInclude());
        Assets.RegisterAssetLoader(new AssetLoaderShaderHLSL(Rendering, (string includeName) =>
        {
            if (Assets.TryLoadRaw(includeName, out ReadOnlySpan<byte> data))
            {
                return Encoding.UTF8.GetString(data);
            }
            throw new Exception($"Can not find the include file: {includeName}");
        }));
        Assets.RegisterAssetLoader(new AssetLoaderShaderSlang(Rendering, Assets));
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

    private void OnSystemMainWindowResize(uint2 size)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try
            {
                _systems[i].OnMainWindowResize(size);
            }
            catch (Exception e)
            {
                Log.Error($"Error when resize system {_systems[i].GetType().Name}: ");
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

    private void MainWindowResize(uint2 size)
    {
        _shouldResize = true;
        _windowSize = size;
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
        _isRunning = false;
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
