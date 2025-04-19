

using Alco.Editor.ViewModels;
using Alco.Editor.Views;
using Alco.Engine;
using Alco.Project;
using Alco.Project.Mcp;
using Avalonia;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

#pragma warning disable CS8618

namespace Alco.Editor
{
    public partial class App : Application
    {
        private readonly EditorPlatform _platform;
        private readonly WebApplication? _mcpServer;

        public static App Main
        {
            get => Current as App ?? throw new InvalidOperationException("App is not initialized");
        }

        public static AlcoProject? CurrentProject
        {
            get => Main.Engine?.Project;
        }

        public Views.Editor EditorWindow { get; private set; }
        public EditorEngine Engine { get; private set; }
        public EditorPreference Preference { get; private set; }



        public App()
        {
            if (!Design.IsDesignMode)
            {
                _platform = new EditorPlatform();
                GameEngineSetting setting = GameEngineSetting.CreateGPUWithoutView();
                setting.Platform = _platform;
                Engine = new EditorEngine(setting);
                Preference = new EditorPreference(Engine);
            }
            else
            {
                Engine = null!;
                Preference = null!;
            }

            try
            {
                AlcoProjectMcpTools.SetContext(Engine);

                var builder = WebApplication.CreateBuilder();
                builder.Services.AddMcpServer().
                WithTools<AlcoProjectMcpTools>().
                WithPrompts<AlcoProjectMcpPrompts>().
                WithHttpTransport();

                _mcpServer = builder.Build();
                _mcpServer.MapMcp();
                // not block main thread
                StartMcpServer(_mcpServer);
            }
            catch (Exception e)
            {
                Log.Error("Failed to start Mcp server", e);
            }
        }

        private async void StartMcpServer(WebApplication mcpServer)
        {
            Log.Info($"Running Mcp server on {string.Join(", ", mcpServer.Urls)}");
            await mcpServer.RunAsync();
            Log.Info("Mcp server stoped");
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                EditorWindow = new Views.Editor
                {
                    DataContext = new ViewModels.Editor(),
                };

                EditorWindow.Closed += OnClose;

                // Add key event handler for F11
                EditorWindow.KeyDown += OnEditorKeyDown;

                desktop.MainWindow = EditorWindow;

                _platform.SetWindow(EditorWindow);
                Engine.Run();
            }

            base.OnFrameworkInitializationCompleted();
        }

        public async ValueTask ShowOpenProjectDialog()
        {
            if (EditorWindow == null) return;

            var files = await EditorWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Project",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Alco Project") { Patterns = new[] { "*.alco" } }]
            });

            if (files.Count > 0)
            {
                var file = files[0];
                if (file.TryGetLocalPath() is string path)
                {
                    await Engine.OpenProjectAsync(path);
                }
            }
        }

        private void DisableAvaloniaDataAnnotationValidation()
        {
            // Get an array of plugins to remove
            var dataValidationPluginsToRemove =
                BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

            // remove each entry found
            foreach (var plugin in dataValidationPluginsToRemove)
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }
        }

        private void OnClose(object? sender, EventArgs e)
        {
            Preference?.Save();
            Engine?.Dispose();
            _mcpServer?.StopAsync();
        }

        private void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                OpenTestGpuWindow();
                e.Handled = true;
            }
        }

        public void OpenTestGpuWindow()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var gpuWindow = new TestGpuWindow();
                gpuWindow.Show();
            }
        }
    }
}