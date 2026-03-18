namespace Alco.Rendering;

/// <summary>
/// Represents a 3D model containing mesh and material data.
/// </summary>
public sealed class Model3D : AutoDisposable
{
    /// <summary>
    /// Gets the mesh of the model.
    /// </summary>
    public Mesh Mesh { get; }

    /// <summary>
    /// Gets the material of the model.
    /// </summary>
    public Material Material { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Model3D"/> class.
    /// </summary>
    /// <param name="mesh">The mesh of the model.</param>
    /// <param name="material">The material of the model.</param>
    public Model3D(Mesh mesh, Material material)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        Material = material ?? throw new ArgumentNullException(nameof(material));
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Mesh.Dispose();
            Material.Dispose();
        }
    }
}
