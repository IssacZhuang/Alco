#nullable enable

using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// The composition layer (.amdl): loading a model asset resolves its mesh stream and the
/// bound material assets through the asset system, with slot lookup and default fallback
/// working on top. The mesh asset is written by the real cooker; the mesh asset stays
/// header-only (no GPU device). Materials load through the engine's material loader
/// (a NoGPU engine; textures are dummies there).
/// </summary>
public class TestModelAsset
{
    private static MeshAssetBuildData CreateQuadBuildData()
    {
        // Two material slots over one quad: "wall" covers the first triangle, "roof" the second.
        VertexPBR[] vertices =
        [
            new() { Position = new Vector3(0, 0, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(0, 0), Tangent = new Vector4(1, 0, 0, 1) },
            new() { Position = new Vector3(1, 0, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(1, 0), Tangent = new Vector4(1, 0, 0, 1) },
            new() { Position = new Vector3(1, 1, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(1, 1), Tangent = new Vector4(1, 0, 0, 1) },
            new() { Position = new Vector3(0, 1, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(0, 1), Tangent = new Vector4(1, 0, 0, 1) },
        ];
        byte[] vertexBytes = new byte[vertices.Length * sizeof(float) * 12];
        int cursor = 0;
        void Write(Vector3 value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor), value.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor + 4), value.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor + 8), value.Z);
            cursor += 12;
        }
        foreach (VertexPBR vertex in vertices)
        {
            Write(vertex.Position);
            Write(vertex.Normal);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor), vertex.UV.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor + 4), vertex.UV.Y);
            cursor += 8;
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor), vertex.Tangent.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor + 4), vertex.Tangent.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor + 8), vertex.Tangent.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(vertexBytes.AsSpan(cursor + 12), vertex.Tangent.W);
            cursor += 16;
        }

        return new MeshAssetBuildData
        {
            Name = "test_quad",
            SourceHash = 0x0123456789ABCDEF,
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = vertexBytes,
                    Indices = [0, 1, 2, 0, 2, 3],
                    MaxError = 0.0f,
                    SubMeshes =
                    [
                        new MeshSubMeshMeta("wall", 0, 3),
                        new MeshSubMeshMeta("roof", 3, 3),
                    ],
                },
            ],
        };
    }

    private static string CreateAssetDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amdl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        using (FileStream meshFile = File.Create(Path.Combine(directory, "house.amsh")))
        {
            MeshAssetWriter.Write(CreateQuadBuildData(), meshFile);
        }

        const string pbrType = "\"$type\": \"Alco.World3D.PbrMaterialAsset\"";
        File.WriteAllText(Path.Combine(directory, "wall.amat"),
            $$"""{ {{pbrType}}, "version": "1.0", "name": "wall", "roughnessFactor": 0.9 }""");
        File.WriteAllText(Path.Combine(directory, "shared.amat"),
            $$"""{ {{pbrType}}, "version": "1.0", "name": "shared" }""");
        File.WriteAllText(Path.Combine(directory, "default.amat"),
            $$"""{ {{pbrType}}, "version": "1.0", "name": "default" }""");

        return directory;
    }

    /// <summary>
    /// Runs the test body against a fresh NoGPU engine over the temp directory, then
    /// tears down: the mesh stream keeps the .amsh open for positional LOD reads, so it
    /// is unloaded (disposing its reader) before the directory is deleted.
    /// </summary>
    private static void RunWithAssets(Action<AssetSystem, string> test)
    {
        string directory = CreateAssetDirectory();
        GameEngine? engine = null;
        try
        {
            engine = new GameEngine(GameEngineSetting.CreateNoGPU());
            AssetSystem assets = engine.AssetSystem;
            assets.AddFileSource(new DirectoryFileSource(directory));
            // Mesh/model loaders are the pipeline family's; the material loader is
            // engine infrastructure (GameEngine's default loaders).
            World3DAssetPipeline.RegisterLoaders(assets, engine.RenderingSystem);
            test(assets, directory);
        }
        finally
        {
            engine?.AssetSystem.Unload("house.amsh");
            engine?.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadResolvesMeshSlotsAndDefault()
    {
        RunWithAssets((assets, directory) =>
        {
            File.WriteAllText(Path.Combine(directory, "house.amdl"), """
                {
                    "version": "1.0",
                    "mesh": "house.amsh",
                    "defaultMaterial": "default.amat",
                    "slots": [
                        { "name": "wall", "material": "wall.amat" },
                        { "name": "misc", "material": "shared.amat" }
                    ]
                }
                """);

            ModelAsset model = assets.Load<ModelAsset>("house.amdl");

            Assert.Multiple(() =>
            {
                Assert.That(model.Name, Is.EqualTo("house"));
                Assert.That(model.Mesh.LodCount, Is.EqualTo(1));

                // Explicit binding, case-insensitive slot lookup.
                Assert.That(model.TryGetMaterial("wall", out MaterialAsset wall), Is.True);
                Assert.That(wall, Is.SameAs(assets.Load<MaterialAsset>("wall.amat")));

                // Unbound mesh slots ("roof" has no binding) fall back to the default material.
                Assert.That(model.TryGetMaterial("roof", out MaterialAsset roofDefault), Is.True);
                Assert.That(roofDefault, Is.SameAs(model.DefaultMaterial));

                // Bindings targeting slot names the mesh does not carry stay inert.
                Assert.That(model.TryGetMaterial("does-not-exist", out MaterialAsset fallback), Is.True);
                Assert.That(fallback, Is.SameAs(model.DefaultMaterial));

                Assert.That(model.GetUnboundSlotNames(), Is.EqualTo(new[] { "roof" }));

                // Distinct materials across bindings + default.
                Assert.That(model.EnumerateMaterials().Count, Is.EqualTo(3));
            });
        });
    }

    [Test]
    public void LoadFailsWhenAReferencedMaterialIsMissing()
    {
        RunWithAssets((assets, directory) =>
        {
            File.WriteAllText(Path.Combine(directory, "broken.amdl"), """
                { "version": "1.0", "mesh": "house.amsh", "slots": [ { "name": "wall", "material": "absent.amat" } ] }
                """);

            Assert.That(() => assets.Load<ModelAsset>("broken.amdl"), Throws.TypeOf<AssetLoadException>());
        });
    }

    [Test]
    public void LoadFailsWhenMeshReferenceIsMissing()
    {
        RunWithAssets((assets, directory) =>
        {
            File.WriteAllText(Path.Combine(directory, "meshless.amdl"), """
                { "version": "1.0", "mesh": "absent.amsh" }
                """);

            Assert.That(() => assets.Load<ModelAsset>("meshless.amdl"), Throws.TypeOf<AssetLoadException>());
        });
    }

    [Test]
    public void ModelAssetsReuseCachedMaterialInstances()
    {
        RunWithAssets((assets, directory) =>
        {
            string modelJson = """
                {
                    "version": "1.0",
                    "mesh": "house.amsh",
                    "slots": [
                        { "name": "wall", "material": "shared.amat" },
                        { "name": "roof", "material": "shared.amat" }
                    ]
                }
                """;
            File.WriteAllText(Path.Combine(directory, "a.amdl"), modelJson);
            File.WriteAllText(Path.Combine(directory, "b.amdl"), modelJson);

            ModelAsset a = assets.Load<ModelAsset>("a.amdl");
            ModelAsset b = assets.Load<ModelAsset>("b.amdl");

            Assert.Multiple(() =>
            {
                // Two slots sharing one material file share the cached instance...
                Assert.That(a.TryGetMaterial("wall", out MaterialAsset aw), Is.True);
                Assert.That(a.TryGetMaterial("roof", out MaterialAsset ar), Is.True);
                Assert.That(aw, Is.SameAs(ar));
                // ...and so do different models referencing the same file.
                Assert.That(b.TryGetMaterial("wall", out MaterialAsset bw), Is.True);
                Assert.That(bw, Is.SameAs(aw));
                // Without a default material, an unbound slot resolves to nothing.
                Assert.That(a.TryGetMaterial("missing", out MaterialAsset none), Is.False);
                Assert.That(none, Is.Null);
            });
        });
    }
}
