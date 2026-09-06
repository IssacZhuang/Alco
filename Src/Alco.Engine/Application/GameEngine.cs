using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;
using Alco.IO;
using System.Text;
using Alco.Audio;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime;
using Alco.Profiler;


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
    private readonly AssetSystem _assetSystem;

    private readonly RenderingSystem _renderingSystem;
    private readonly PriorityList<IEngineSystem> _systems = new PriorityList<IEngineSystem>((x, y) => x.Order.CompareTo(y.Order));

    private readonly PngReadbackPipeline _captureReadback;
    private readonly RenderCaptureSystem _renderCaptureSystem;
    private readonly SwapchainCaptureSystem _swapchainCaptureSystem;

    private readonly Platform _platform;
    private readonly Input _input;
    private readonly View _mainView;
    private readonly ViewPresenter _mainPresenter;

    #endregion


    #region  Internal Controllers
    private EngineProfiler _profiler;
    private readonly GameSynchronizationContext _synchronizationContext;
    private readonly JsonSerializerOptions _preferenceSerializerOption;

    #endregion


    #region  State
    private int _mainThreadId = Environment.CurrentManagedThreadId;

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
    /// The platform integration hosting the engine main loop, input, and views.
    /// </summary>
    public Platform Platform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _platform;
    }

    /// <summary>
    /// The main view singleton of the game
    /// </summary>
    public View MainView
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainView;
    }

    /// <summary>
    /// The presenter of the main view: owns the swapchain surface (acquire, present, resize).
    /// </summary>
    public ViewPresenter MainPresenter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mainPresenter;
    }

    /// <summary>
    /// The asset manager of the game<br/>
    /// Which provides the asset loading and caching
    /// </summary>
    public AssetSystem AssetSystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _assetSystem;
    }

    public BuiltInAssets BuiltInAssets
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _builtInAssets;
    }

    /// <summary>
    /// The input singleton of the game
    /// </summary>
    public Input Input
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input;
    }

    /// <summary>
    /// The high-level graphics API of the game<br/>
    /// </summary>
    public RenderingSystem RenderingSystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderingSystem;
    }

    /// <summary>
    /// The shared PNG readback pipeline behind the capture systems. Pumped by the
    /// engine each update; capture owners register a completion callback when
    /// beginning a read.
    /// </summary>
    public PngReadbackPipeline CaptureReadback
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _captureReadback;
    }

    /// <summary>
    /// The engine-managed render-graph capture system (content chain of the active
    /// render pipeline, ImGui overlay excluded). The host keeps
    /// <see cref="RenderCaptureSystem.ActivePipeline"/> current.
    /// </summary>
    public RenderCaptureSystem RenderCaptureSystem
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _renderCaptureSystem;
    }

    /// <summary>
    /// The engine-managed swapchain capture system (the exact pixels about to be
    /// presented, ImGui overlay included).
    /// </summary>
    public SwapchainCaptureSystem SwapchainCapture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _swapchainCaptureSystem;
    }

    /// <summary>
    /// Gets the average main-loop frame rate measured from wall-clock frame intervals.
    /// </summary>
    public int FrameRate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _profiler.FPS;
    }

    /// <summary>
    /// Gets the one-percent-low frame rate calculated from recent wall-clock frame intervals.
    /// </summary>
    public int OnePercentLowFrameRate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _profiler.OnePercentLowFPS;
    }

    /// <summary>
    /// Gets the average wall-clock duration between main-loop frames, in seconds.
    /// </summary>
    public float FrameTime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _profiler.FrameTime;
    }

    /// <summary>
    /// Gets the 99th-percentile wall-clock frame duration from recent samples, in seconds.
    /// </summary>
    public float P99FrameTime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _profiler.P99FrameTime;
    }

    /// <summary>
    /// Gets the process-wide method profiler used by instrumented builds.
    /// </summary>
    public MethodProfilerRuntime MethodProfiler => MethodProfilerRuntime.Instance;

    /// <summary>
    /// Check if the engine is disposed
    /// </summary>
    public bool IsDisposed => _disposed != 0;

    /// <summary>
    /// The setting of the game engine
    /// </summary>
    public GameEngineSetting Setting => _setting;

    /// <summary>
    /// The main thread id of the game engine
    /// </summary>
    public int MainThreadId => _mainThreadId;

    /// <summary>
    /// Check if the current thread is the main thread
    /// </summary>
    public bool IsMainThread => Environment.CurrentManagedThreadId == _mainThreadId;

    #endregion

    public GameEngine(GameEngineSetting setting)
    {
        _setting = setting;
        _synchronizationContext = new GameSynchronizationContext();
        _assetSystem = new AssetSystem(this, _setting.Assets.IsProfilingEnabled);

        _graphicsDevice = CreateGraphicsDevice(_setting.Graphics, 0);

        _renderingSystem = new RenderingSystem(
            this,
            _graphicsDevice,
            _setting.Graphics.PreferredHDRFormat,
            _setting.Graphics.PreferredDepthStencilFormat,
            // Module-name probes resolve via dashed-name matching over
            // asset-system assets.
            ShaderModuleResolver.Create(
                path => _assetSystem.TryGetStream(path, out Stream? stream) ? stream : null,
                () => _assetSystem.AllAssetNames),
            CreateShaderCacheDirectory(_setting.Graphics)
            );

        _builtInAssets = new BuiltInAssets(_assetSystem, _renderingSystem);

        _audioDevice = CreateAudioDevice(_setting.Audio);

        foreach (var fileSource in CreateDefaultFileSources())
        {
            _assetSystem.AddFileSource(fileSource);
        }

        foreach (var assetLoader in CreateDefaultAssetLoaders())
        {
            _assetSystem.RegisterAssetLoader(assetLoader);
        }

        foreach (var assetHotReloader in CreateDefaultAssetHotReloaders())
        {
            _assetSystem.RegisterAssetHotReloader(assetHotReloader);
        }

        _platform = _setting.Platform ?? new Sdl3Platform();
        _platform.TargetFrameRate = _setting.TargetFrameRate;
        _input = _platform.Input;
        _platform.OnAudioDefaultDeviceChanged += () => _audioDevice.NotifyDefaultDeviceChanged();

        _profiler = new EngineProfiler();

        //main view
        _mainView = CreateView(_setting.View);
        _mainPresenter = new ViewPresenter(_mainView);

        // Engine-managed capture systems: one shared PNG readback pipeline serves both
        // the render-graph captures (content chain, ImGui excluded) and the swapchain
        // captures (presented frame, ImGui included). Hosts assign the active render
        // pipeline to RenderCaptureSystem when they build their pipelines. Headless
        // (no swapchain) swapchain captures fall back to the render-graph chain tail.
        _captureReadback = new PngReadbackPipeline(_graphicsDevice);
        _renderCaptureSystem = new RenderCaptureSystem(this, _captureReadback);
        AddSystem(_renderCaptureSystem);
        _swapchainCaptureSystem = new SwapchainCaptureSystem(this, _captureReadback)
        {
            OffscreenFallback = () => _renderCaptureSystem.RequestCaptureAsync(),
        };
        AddSystem(_swapchainCaptureSystem);

        // Auto-initialize debug stats overlay as an engine-managed system.
        AddSystem(new DebugStatsSystem(this));

        _preferenceSerializerOption = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        foreach (var converter in CreateDefaultJsonConverters())
        {
            _preferenceSerializerOption.Converters.Add(converter);
        }
    }


    #region  Lifecycle

    /// <summary>
    /// Start the main loop of the game
    /// </summary>
    [STAThread]
    public void Run()
    {
        InternalRun();
    }


    /// <summary>
    /// The loop with graphics, which is used for the client
    /// </summary>
    private void InternalRun()
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch (Exception e)
        {

            Log.Error("[Set Process Priority Error]", e);
        }


        _mainThreadId = Environment.CurrentManagedThreadId;

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

    /// <summary>
    /// Called when player exit the game
    /// </summary>
    protected virtual void OnStop()
    {

    }

    private void InternalTick(float delta)
    {
        if (!_setting.EnableMethodProfiling)
        {
            ExecuteTickBody(delta);
            return;
        }

        MethodProfilerTickToken profilerTick = MethodProfiler.BeginTick();
        if (!profilerTick.IsValid)
        {
            ExecuteTickBody(delta);
            return;
        }

        try
        {
            ExecuteTickBody(delta);
        }
        finally
        {
            MethodProfiler.EndTick(profilerTick);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteTickBody(float delta)
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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InternalUpdate(float delta)
    {
        _profiler.Update(Stopwatch.GetTimestamp());

        // Process any callbacks queued for the main thread
        _synchronizationContext.ProcessCallbacks();

        // Pump the shared PNG readback pipeline: delivers finished captures to the
        // capture systems' completion callbacks.
        _captureReadback.Pump();

        _audioDevice.Poll(delta);

        // Acquire the swapchain surface for this frame
        _mainPresenter.BeginFrame();

        EventOnUpdate?.Invoke(delta);

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

        OnSystemEndFrame(delta);

        // Per-frame resource disposal (deferred GPU resource destruction)
        EventOnEndFrame?.Invoke();

        // Present the frame
        _mainPresenter.EndFrame();
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
        _mainPresenter.Dispose();
        MainView.Close();
        _platform.Dispose();
        _captureReadback.Dispose();

        EventOnDispose?.Invoke();
        GC.SuppressFinalize(this);
    }

    private void OnSystemStart()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try { _systems[i].OnStart(); }
            catch (Exception e) { Log.Error($"Error when start system {_systems[i].GetType().Name}: "); Log.Error(e); TryErrorStop(); }
        }
    }

    private void OnSystemTick(float delta)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try { _systems[i].OnTick(delta); }
            catch (Exception e) { Log.Error($"Error when tick system {_systems[i].GetType().Name}: "); Log.Error(e); TryErrorStop(); }
        }
    }

    private void OnSystemUpdate(float delta)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try { _systems[i].OnUpdate(delta); }
            catch (Exception e) { Log.Error($"Error when update system {_systems[i].GetType().Name}: "); Log.Error(e); TryErrorStop(); }
        }
    }

    private void OnSystemEndFrame(float deltaTime)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try { _systems[i].OnEndFrame(deltaTime); }
            catch (Exception e) { Log.Error($"Error when end frame system {_systems[i].GetType().Name}: "); Log.Error(e); TryErrorStop(); }
        }
    }

    private void OnSystemStop()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try { _systems[i].OnStop(); }
            catch (Exception e) { Log.Error($"Error when stop system {_systems[i].GetType().Name}: "); Log.Error(e); TryErrorStop(); }
        }
    }

    private void OnSystemDispose()
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            try { _systems[i].Dispose(); }
            catch (Exception e) { Log.Error($"Error when dispose system {_systems[i].GetType().Name}: "); Log.Error(e); TryErrorStop(); }
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

    public bool TryGetSystem<T>([NotNullWhen(true)] out T? system)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            if (_systems[i] is T s)
            {
                system = s;
                return true;
            }
        }
        system = default;
        return false;
    }

    public static void DoGarbageCollection(bool compactLOH = false)
    {
        if (compactLOH)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.Default;
        }
        else
        {
            GC.Collect();
        }
    }

    public static MemoryPressure GetMemoryPressure()
    {
        GCMemoryInfo gCMemoryInfo = GC.GetGCMemoryInfo();
        if ((double)gCMemoryInfo.MemoryLoadBytes >= (double)gCMemoryInfo.HighMemoryLoadThresholdBytes * 0.9)
        {
            return MemoryPressure.High;
        }
        if ((double)gCMemoryInfo.MemoryLoadBytes >= (double)gCMemoryInfo.HighMemoryLoadThresholdBytes * 0.7)
        {
            return MemoryPressure.Medium;
        }
        return MemoryPressure.Low;
    }

    /// <summary>
    /// Throws an exception if the current thread is not the main thread
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called from a non-main thread</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfNotMainThread()
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException($"This operation must be called from the main thread (ID: {_mainThreadId}), but was called from thread ID: {Environment.CurrentManagedThreadId}");
        }
    }

    /// <summary>
    /// Save a preference to the application data folder as JSON
    /// </summary>
    /// <typeparam name="T">The type of the preference value</typeparam>
    /// <param name="applicationName">The name of the application</param>
    /// <param name="key">The preference key</param>
    /// <param name="preference">The preference value to save</param>
    public void SavePreference<T>(string applicationName, string key, T preference) where T : new()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), applicationName);
            Directory.CreateDirectory(path);
            string filePath = Path.Combine(path, $"{key}.json");

            string json = JsonSerializer.Serialize(preference, _preferenceSerializerOption);
            File.WriteAllText(filePath, json);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save preference '{key}' for application '{applicationName}': ", e);
        }
    }

    /// <summary>
    /// Get a preference from the application data folder as JSON
    /// </summary>
    /// <typeparam name="T">The type of the preference value</typeparam>
    /// <param name="applicationName">The name of the application</param>
    /// <param name="key">The preference key</param>
    /// <returns>The preference value, or a new instance if not found</returns>
    public T LoadPreference<T>(string applicationName, string key) where T : new()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), applicationName);
        string filePath = Path.Combine(path, $"{key}.json");

        if (!File.Exists(filePath))
        {
            return new T();
        }

        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, _preferenceSerializerOption) ?? new T();
        }
        catch (Exception e)
        {
            // If deserialization fails, log error and return a new instance
            Log.Error($"Failed to load preference '{key}' for application '{applicationName}': ", e);
            return new T();
        }
    }

    #endregion

}
