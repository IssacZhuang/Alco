using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Asset loader for glTF 2.0 model scenes (.gltf / .glb).
/// Creates a <see cref="ModelScene"/>: GPU meshes, textures and a flattened draw list.
/// <br/>Only the engine-relevant material subset is realized (base color, normal,
/// metallic-roughness and emissive textures, factors, alpha mode); other extensions are ignored.
/// <br/>Textures stream in asynchronously: the scene is returned immediately with null
/// textures (the pipeline renders fallbacks) and each texture is decoded off-thread
/// (rate-limited by <see cref="Environment.ProcessorCount"/>) and assigned on the main thread
/// via the installed <see cref="SynchronizationContext"/>. <see cref="ModelScene.LoadingCompletion"/>
/// completes when streaming finishes. Albedo and emissive textures decode as sRGB; normal and
/// metallic-roughness textures decode as linear data.
/// </summary>
public sealed class AssetLoaderModelGltf : BaseAssetLoader<ModelScene>
{
    /// <summary>Which material slot a streaming texture belongs to.</summary>
    private enum TextureRole
    {
        Albedo,
        Normal,
        MetallicRoughness,
        Emissive,
    }

    private static readonly string[] Extensions = [FileExt.ModelGLTF, FileExt.ModelGLB];

    /// <summary>
    /// Process-wide limit of concurrent texture decodes; decoding is CPU-bound and a
    /// 4K decode peaks at ~64MB of native memory, so unbounded fan-out must not happen.
    /// </summary>
    private static readonly SemaphoreSlim TextureLoadSlots = new(Math.Max(2, Environment.ProcessorCount));

    private readonly RenderingSystem _renderingSystem;

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Model.glTF";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetLoaderModelGltf"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create meshes and textures.</param>
    public AssetLoaderModelGltf(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem ?? throw new ArgumentNullException(nameof(renderingSystem));
    }

    /// <inheritdoc/>
    public override unsafe object CreateAsset(in AssetLoadContext context)
    {
        AssetSystem assetSystem = context.AssetSystem;
        string directory = GetDirectory(context.Filename);
        var bufferHandles = new List<SafeMemoryHandle>();

        try
        {
            using GltfModel model = GltfDecodeUtility.DecodeAuto(context.GetData(), ResolveBuffer);

            // Meshes: one PrimitiveMesh per primitive, deduplicated by glTF mesh index.
            var meshTable = new PrimitiveMesh[model.Meshes.Count][];
            var ownedMeshes = new List<PrimitiveMesh>();
            for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                GltfMesh mesh = model.Meshes[meshIndex];
                var primitiveMeshes = new PrimitiveMesh[mesh.PrimitiveCount];
                for (int i = 0; i < mesh.PrimitiveCount; i++)
                {
                    int primitiveIndex = mesh.PrimitiveStart + i;
                    string meshName = string.IsNullOrEmpty(mesh.Name) ? $"gltf_mesh_{meshIndex}" : mesh.Name;
                    if (mesh.PrimitiveCount > 1)
                    {
                        meshName = $"{meshName}_{i}";
                    }
                    PrimitiveMesh primitiveMesh = _renderingSystem.CreatePrimitiveMesh(
                        model.GetVertices(primitiveIndex),
                        model.GetIndices(primitiveIndex),
                        meshName);
                    primitiveMeshes[i] = primitiveMesh;
                    ownedMeshes.Add(primitiveMesh);
                }
                meshTable[meshIndex] = primitiveMeshes;
            }

            // Materials, with a fallback default for primitives that lack one. Textures
            // stay null here and stream in asynchronously after the scene returns. The
            // same image may serve different roles (e.g. shared as albedo and MR source),
            // so the streaming groups are keyed by (image, role).
            var materials = new List<ModelMaterial>(model.Materials.Count + 1);
            var materialsByImage = new Dictionary<(int ImageIndex, TextureRole Role), (AddressMode Wrap, List<ModelMaterial> Targets)>();
            foreach (GltfMaterial gltfMaterial in model.Materials)
            {
                var material = new ModelMaterial
                {
                    Name = gltfMaterial.Name,
                    BaseColorFactor = gltfMaterial.BaseColorFactor,
                    MetallicFactor = gltfMaterial.MetallicFactor,
                    RoughnessFactor = gltfMaterial.RoughnessFactor,
                    EmissiveFactor = gltfMaterial.EmissiveFactor,
                    AlphaMode = gltfMaterial.AlphaMode,
                    AlphaCutoff = gltfMaterial.AlphaCutoff,
                    DoubleSided = gltfMaterial.DoubleSided,
                };
                materials.Add(material);

                AddTextureTarget(gltfMaterial.BaseColorImageIndex, TextureRole.Albedo, gltfMaterial.WrapS);
                AddTextureTarget(gltfMaterial.NormalImageIndex, TextureRole.Normal, gltfMaterial.NormalWrapS);
                AddTextureTarget(gltfMaterial.MetallicRoughnessImageIndex, TextureRole.MetallicRoughness, gltfMaterial.MetallicRoughnessWrapS);
                AddTextureTarget(gltfMaterial.EmissiveImageIndex, TextureRole.Emissive, gltfMaterial.EmissiveWrapS);

                void AddTextureTarget(int imageIndex, TextureRole role, AddressMode wrap)
                {
                    if ((uint)imageIndex >= (uint)model.Images.Count)
                    {
                        return;
                    }
                    var key = (imageIndex, role);
                    if (!materialsByImage.TryGetValue(key, out (AddressMode Wrap, List<ModelMaterial> Targets) group))
                    {
                        group = (wrap, new List<ModelMaterial>());
                        materialsByImage[key] = group;
                    }
                    group.Targets.Add(material);
                }
            }
            int defaultMaterialIndex = materials.Count;
            materials.Add(new ModelMaterial
            {
                Name = "default",
                MetallicFactor = 0.0f,
                RoughnessFactor = 0.9f,
            });

            // Flattened draw items.
            var drawItems = new List<ModelDrawItem>(model.DrawItems.Count);
            foreach (GltfDrawItem drawItem in model.DrawItems)
            {
                GltfMesh mesh = model.Meshes[drawItem.MeshIndex];
                PrimitiveMesh[] primitiveMeshes = meshTable[drawItem.MeshIndex];
                for (int i = 0; i < mesh.PrimitiveCount; i++)
                {
                    int materialIndex = model.GetMaterialIndex(mesh.PrimitiveStart + i);
                    if ((uint)materialIndex >= (uint)model.Materials.Count)
                    {
                        materialIndex = defaultMaterialIndex;
                    }
                    drawItems.Add(new ModelDrawItem(primitiveMeshes[i], materialIndex, drawItem.World));
                }
            }

            var scene = new ModelScene(
                materials,
                drawItems,
                [.. ownedMeshes],
                model.BoundsMin,
                model.BoundsMax);
            StartTextureStreaming(scene, model, materialsByImage, assetSystem, directory);
            return scene;
        }
        finally
        {
            foreach (SafeMemoryHandle handle in bufferHandles)
            {
                handle.Dispose();
            }
        }

        bool ResolveBuffer(string uri, out ReadOnlySpan<byte> data)
        {
            string path = CombinePath(directory, uri);
            if (!assetSystem.TryLoadRaw(path, out SafeMemoryHandle handle))
            {
                data = default;
                return false;
            }
            bufferHandles.Add(handle);
            data = handle.AsReadOnlySpan();
            return true;
        }
    }

    /// <summary>
    /// Kick asynchronous streaming of all referenced textures. Each unique (image, role) pair
    /// is decoded and uploaded on thread-pool threads (rate-limited); the main-thread
    /// continuation assigns the texture to its materials and hands ownership to the scene.
    /// Missing image files are tolerated: those materials keep their fallbacks.
    /// </summary>
    private void StartTextureStreaming(
        ModelScene scene,
        GltfModel model,
        Dictionary<(int ImageIndex, TextureRole Role), (AddressMode Wrap, List<ModelMaterial> Targets)> materialsByImage,
        AssetSystem assetSystem,
        string directory)
    {
        if (materialsByImage.Count == 0)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        var tasks = new List<Task>(materialsByImage.Count);
        foreach (((int imageIndex, TextureRole role), (AddressMode wrap, List<ModelMaterial> targets)) in materialsByImage)
        {
            GltfImage image = model.Images[imageIndex];

            // Resolve external files up front so missing ones keep the fallbacks.
            string? path = null;
            if (image.Uri != null)
            {
                path = CombinePath(directory, image.Uri);
                if (!assetSystem.IsFileExist(path))
                {
                    continue;
                }
            }
            else if (image.EmbeddedData.IsEmpty)
            {
                continue;
            }

            Task<Texture2D?> imageTask = LoadImageTextureAsync(assetSystem, image, path, wrap, role);
            tasks.Add(AssignImageTextureAsync(scene, targets, role, imageTask));
        }

        if (tasks.Count == 0)
        {
            return;
        }
        scene.SetLoadingCompletion(LogWhenStreamed(Task.WhenAll(tasks), tasks.Count, watch));
    }

    /// <summary>Decode and upload one image off-thread; null on failure (fallback kept).</summary>
    private async Task<Texture2D?> LoadImageTextureAsync(
        AssetSystem assetSystem, GltfImage image, string? path, AddressMode wrap, TextureRole role)
    {
        await TextureLoadSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => DecodeImageTexture(assetSystem, image, path, wrap, role)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to stream glTF texture '{image.Name}': {ex.Message}");
            return null;
        }
        finally
        {
            TextureLoadSlots.Release();
        }
    }

    /// <summary>
    /// Runs on the main thread (via the installed SynchronizationContext): hand the texture
    /// to the scene and assign it to its materials. Both are skipped after scene disposal.
    /// </summary>
    private static async Task AssignImageTextureAsync(
        ModelScene scene, List<ModelMaterial> targets, TextureRole role, Task<Texture2D?> imageTask)
    {
        Texture2D? texture = await imageTask;
        if (texture == null || !scene.TakeOwnedTexture(texture))
        {
            return;
        }
        foreach (ModelMaterial target in targets)
        {
            switch (role)
            {
                case TextureRole.Albedo:
                    target.AlbedoTexture = texture;
                    break;
                case TextureRole.Normal:
                    target.NormalTexture = texture;
                    break;
                case TextureRole.MetallicRoughness:
                    target.MetallicRoughnessTexture = texture;
                    break;
                case TextureRole.Emissive:
                    target.EmissiveTexture = texture;
                    break;
            }
        }
    }

    /// <summary>Report streaming duration as part of <see cref="ModelScene.LoadingCompletion"/>.</summary>
    private static async Task LogWhenStreamed(Task whenAll, int count, Stopwatch watch)
    {
        await whenAll;
        Log.Success($"glTF texture streaming finished: {count} images in {watch.ElapsedMilliseconds}ms");
    }

    /// <summary>Read, decode and upload one image. Runs on a thread-pool thread.</summary>
    private Texture2D? DecodeImageTexture(AssetSystem assetSystem, GltfImage image, string? path, AddressMode wrap, TextureRole role)
    {
        var option = ImageLoadOption.Default with
        {
            // Albedo and emissive are sRGB color data; normal and metallic-roughness maps are linear.
            Format = role is TextureRole.Albedo or TextureRole.Emissive ? PixelFormat.RGBA8UnormSrgb : PixelFormat.RGBA8Unorm,
            AddressMode = wrap,
            // Anisotropic filtering against minification moiré on walls and ground
            // planes seen at grazing angles.
            Anisotropy = 8,
            Name = image.Name,
        };

        if (path != null)
        {
            if (!assetSystem.TryLoadRaw(path, out SafeMemoryHandle handle))
            {
                return null;
            }
            using (handle)
            {
                return _renderingSystem.CreateTexture2DFromFile(handle.AsReadOnlySpan(), option);
            }
        }
        return _renderingSystem.CreateTexture2DFromFile(image.EmbeddedData, option);
    }

    /// <summary>
    /// Get the directory part of an asset path ('/'-separated, trailing slash included).
    /// </summary>
    private static string GetDirectory(string filename)
    {
        int slash = filename.LastIndexOf('/');
        return slash < 0 ? string.Empty : filename[..(slash + 1)];
    }

    /// <summary>
    /// Combine an asset directory with a glTF-relative URI, resolving any '..' segments.
    /// </summary>
    private static string CombinePath(string directory, string uri)
    {
        if (!uri.Contains(".."))
        {
            return directory + uri;
        }

        var segments = new List<string>();
        foreach (string segment in (directory + uri).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                continue;
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }
}
