using System.Numerics;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Asset loader for glTF 2.0 model scenes (.gltf / .glb).
/// Creates a <see cref="ModelScene"/>: GPU meshes, textures and a flattened draw list.
/// <br/>Only the engine-relevant material subset is realized (base color, normal,
/// metallic-roughness and emissive textures, factors, alpha mode); other extensions are ignored.
/// <br/>External textures stream: each is created at its final specification from a header
/// probe and its content uploads in place asynchronously (see
/// <see cref="RenderingSystem.CreateTexture2DStreaming"/>), so texture identities and
/// material bindings are final when the scene returns. Albedo and emissive textures are
/// sRGB color data; normal and metallic-roughness textures are linear.
/// </summary>
public sealed class AssetLoaderModelGltf : BaseAssetLoader<ModelScene>
{
    /// <summary>Which material slot an image belongs to.</summary>
    private enum TextureRole
    {
        Albedo,
        Normal,
        MetallicRoughness,
        Emissive,
    }

    private static readonly string[] Extensions = [FileExt.ModelGLTF, FileExt.ModelGLB];

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
            // are realized before the scene returns (external files stream their content
            // in asynchronously, in place). The same image may serve different roles
            // (e.g. shared as albedo and MR source), so the load groups are keyed by
            // (image, role).
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
                    int primitiveIndex = mesh.PrimitiveStart + i;
                    int materialIndex = model.GetMaterialIndex(primitiveIndex);
                    if ((uint)materialIndex >= (uint)model.Materials.Count)
                    {
                        materialIndex = defaultMaterialIndex;
                    }
                    drawItems.Add(new ModelDrawItem(
                        primitiveMeshes[i],
                        materialIndex,
                        drawItem.World,
                        model.GetBoundsMin(primitiveIndex),
                        model.GetBoundsMax(primitiveIndex)));
                }
            }

            var scene = new ModelScene(
                materials,
                drawItems,
                [.. ownedMeshes],
                model.BoundsMin,
                model.BoundsMax);
            LoadTextures(scene, model, materialsByImage, assetSystem, directory);
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
    /// Realize every referenced image once per (image, role) group and assign it to its
    /// materials; the scene takes ownership of each created texture. External files stream
    /// their content in place, so material bindings are final when this returns. Missing
    /// or undecodable images are tolerated: those materials keep their fallbacks.
    /// </summary>
    private void LoadTextures(
        ModelScene scene,
        GltfModel model,
        Dictionary<(int ImageIndex, TextureRole Role), (AddressMode Wrap, List<ModelMaterial> Targets)> materialsByImage,
        AssetSystem assetSystem,
        string directory)
    {
        foreach (((int imageIndex, TextureRole role), (AddressMode wrap, List<ModelMaterial> targets)) in materialsByImage)
        {
            Texture2D? texture = CreateImageTexture(
                assetSystem, model.Images[imageIndex], directory, role, wrap);
            if (texture == null)
            {
                continue;
            }
            scene.TakeOwnedTexture(texture);
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
    }

    /// <summary>
    /// Create the texture of one image, or null when it is missing or fails to decode.
    /// External images stream from the asset system: the texture is created at its final
    /// specification from a header probe and its content uploads in place asynchronously.
    /// Images whose header cannot be probed fall back to a full synchronous decode, as do
    /// embedded images (their bytes are already in memory).
    /// </summary>
    private Texture2D? CreateImageTexture(
        AssetSystem assetSystem, GltfImage image, string directory, TextureRole role, AddressMode wrap)
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

        try
        {
            if (image.Uri != null)
            {
                string path = CombinePath(directory, image.Uri);
                if (!assetSystem.TryGetStream(path, out Stream? stream))
                {
                    return null;
                }
                try
                {
                    return _renderingSystem.CreateTexture2DStreaming(stream, option);
                }
                catch (ImageDecodeException)
                {
                    // The header could not be probed: fall back to a full synchronous decode.
                    stream.Dispose();
                    if (!assetSystem.TryLoadRaw(path, out SafeMemoryHandle handle))
                    {
                        return null;
                    }
                    using (handle)
                    {
                        return _renderingSystem.CreateTexture2DFromFile(handle.AsReadOnlySpan(), option);
                    }
                }
            }
            if (!image.EmbeddedData.IsEmpty)
            {
                return _renderingSystem.CreateTexture2DFromFile(image.EmbeddedData, option);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to load glTF texture '{image.Name}': {ex.Message}");
            return null;
        }
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
