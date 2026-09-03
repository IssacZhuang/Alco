using Alco.IO;
using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The editor's built-in defaults: the document factories for the asset types the
/// engine ships editors for, the default particle preview environment, the File and
/// Window menus, the particle effect asset templates and the engine's meta types.
/// <see cref="EditorSystem"/> registers this module first, so modules passed to it
/// can override any of these registrations.
/// <br/>The menu items resolve the shell (<see cref="EditorSystem"/>) lazily through
/// the service bag: the module runs while the shell is still being constructed.
/// </summary>
public sealed class BuiltInEditorModule : IEditorModule
{
    /// <inheritdoc />
    public void Register(EditorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        EditorContext context = registry.Context;

        // Documents: the editors the engine ships, by file extension.
        registry.Documents.Register(FileExt.Material, new DelegateDocumentFactory((ctx, path) => new MaterialDocument(ctx, path)));
        registry.Documents.Register(ParticleAssetPipeline.EffectExtension, new DelegateDocumentFactory((ctx, path) => new ParticleEffectDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImagePNG, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImageJPG, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImageBMP, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImageTGA, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImageGIF, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImageHDR, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));
        registry.Documents.Register(FileExt.ImageDDS, new DelegateDocumentFactory((ctx, path) => new TextureDocument(ctx, path)));

        // Services: the default particle preview environment.
        registry.Services.Register<IParticlePreviewEnvironment>(DefaultParticlePreviewEnvironment.Instance);

        // Menus: the shell's File and Window menus.
        registry.Menus.AddItem("File/Open Project...", new EditorMenuItem(
            () => registry.Services.Get<EditorSystem>().RequestOpenProjectDialog()));
        registry.Menus.AddSeparator("File");
        registry.Menus.AddItem("File/Save", new EditorMenuItem(
            () => registry.Services.Get<EditorSystem>().Documents.SaveActive())
        {
            Shortcut = "Ctrl+S",
            IsEnabled = () => registry.Services.Get<EditorSystem>().Documents.ActiveDocument is { IsDirty: true, IsReadOnly: false },
        });
        registry.Menus.AddItem("File/Exit", new EditorMenuItem(() => context.Engine.Stop())
        {
            Shortcut = "Esc",
        });
        registry.Menus.AddItem(EditorSystem.WindowMenuTitle + "/Asset Browser", new EditorMenuItem(() =>
        {
            EditorSystem shell = registry.Services.Get<EditorSystem>();
            shell.AssetBrowserOpen = !shell.AssetBrowserOpen;
        })
        {
            IsChecked = () => registry.Services.Get<EditorSystem>().AssetBrowserOpen,
        });
        registry.Menus.AddItem(EditorSystem.WindowMenuTitle + "/Reset Layout", new EditorMenuItem(
            () => registry.Services.Get<EditorSystem>().RequestResetLayout()));

        // Asset templates: the particle effect starters.
        registry.AssetTemplates.Register(new ParticleEffect2DTemplate());
        registry.AssetTemplates.Register(new ParticleEffect3DTemplate());

        // Meta types: the engine's meta sidecars.
        registry.MetaTypes.Register<Texture2DMeta>();
    }

    /// <summary>The minimal 2D particle effect starter (see <see cref="ParticleEffectTemplates"/>).</summary>
    private sealed class ParticleEffect2DTemplate : IAssetTemplate
    {
        public string DisplayName => "Particle Effect 2D (.afx)";
        public string FileExtension => ParticleAssetPipeline.EffectExtension;
        public string BaseName => "NewEffect2D";
        public string CreateContent(string assetName) => ParticleEffectTemplates.Effect2D;
    }

    /// <summary>The minimal 3D particle effect starter (see <see cref="ParticleEffectTemplates"/>).</summary>
    private sealed class ParticleEffect3DTemplate : IAssetTemplate
    {
        public string DisplayName => "Particle Effect 3D (.afx)";
        public string FileExtension => ParticleAssetPipeline.EffectExtension;
        public string BaseName => "NewEffect3D";
        public string CreateContent(string assetName) => ParticleEffectTemplates.Effect3D;
    }
}
