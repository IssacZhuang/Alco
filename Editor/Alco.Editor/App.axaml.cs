using Alco.Editor.ViewModels;
using Alco.Editor.Views;
using Alco.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;

namespace Alco.Editor
{
    public partial class App : Application
    {
        public static App Main
        {
            get => Current as App ?? throw new InvalidOperationException("App is not initialized");
        }

        public Views.Editor? EditorWindow { get; private set; }
        public EditorEngine Engine { get; }


        public App()
        {
            if (!Design.IsDesignMode)
            {
                Engine = new EditorEngine(GameEngineSetting.CreateGPUWithoutWindow());
            }
            else
            {
                Engine = null!;
            }


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
                EditorWindow.Closed += DisposeEngine;
                desktop.MainWindow = EditorWindow;
            }

            base.OnFrameworkInitializationCompleted();
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

        private void DisposeEngine(object? sender, EventArgs e)
        {
            Engine?.Dispose();
        }
    }
}