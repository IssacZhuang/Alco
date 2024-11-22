using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class BaseMaterial : Material
{
    protected readonly RenderingSystem _system;
    protected readonly ShaderParameterSet _parameters;
    private readonly HashSet<AutoDisposable> _managedResources = new();

    /// <summary>
    /// The name of the material.
    /// </summary>
    public string Name { get; }

    protected BaseMaterial(RenderingSystem system, ShaderReflectionInfo reflectionInfo, string name){
        Name = name;
        _system = system;
        _parameters = new ShaderParameterSet(reflectionInfo);
        UpdateSlotResources(reflectionInfo);
    }

    #region  Set value

    /// <summary>
    /// Try to set the value of the uniform buffer.
    /// </summary>
    /// <param name="name">The shader resource name of the uniform buffer.</param>
    /// <param name="value">The value to set.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>True if the value is set successfully, otherwise false.</returns>
    public unsafe bool TrySetValue<T>(string name, T value) where T : unmanaged
    {
        if (_parameters.TryGetBuffer(name, out GraphicsBuffer? buffer))
        {
            if (buffer.Size < sizeof(T))
            {
                return false;
            }

            buffer.UpdateBuffer(value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Try to set the value of the uniform buffer.
    /// </summary>
    /// <param name="id">The shader resource id of the uniform buffer.</param>
    /// <param name="value">The value to set.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>True if the value is set successfully, otherwise false.</returns>
    public unsafe bool TrySetValue<T>(uint id, T value) where T : unmanaged
    {
        if (_parameters.TryGetBuffer(id, out GraphicsBuffer? buffer))
        {
            if (buffer.Size < sizeof(T))
            {
                return false;
            }

            buffer.UpdateBuffer(value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Set the value of the uniform buffer.
    /// </summary>
    /// <param name="name">The shader resource name of the uniform buffer.</param>
    /// <param name="value">The value to set.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    public unsafe void SetValue<T>(string name, T value) where T : unmanaged
    {
        if (!_parameters.TryGetBuffer(name, out GraphicsBuffer? buffer))
        {
            throw new KeyNotFoundException($"Resource '{name}' not found in shader");
        }

        if (buffer.Size < sizeof(T))
        {
            throw new InvalidOperationException($"The buffer size is not enough for the value.");
        }

        buffer.UpdateBuffer(value);
    }

    /// <summary>
    /// Set the value of the uniform buffer.
    /// </summary>
    /// <param name="id">The shader resource id of the uniform buffer.</param>
    /// <param name="value">The value to set.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    public unsafe void SetValue<T>(uint id, T value) where T : unmanaged
    {
        if (!_parameters.TryGetBuffer(id, out GraphicsBuffer? buffer))
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (buffer.Size < sizeof(T))
        {
            throw new InvalidOperationException($"The buffer size is not enough for the value.");
        }

        buffer.UpdateBuffer(value);
    }

    #endregion

    #region Set texture

    /// <summary>
    /// Try to set the texture.
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>True if the texture is set successfully, otherwise false.</returns>
    public bool TrySetTexture(string name, Texture2D texture)
    {
        return _parameters.TrySetTexture(name, texture);
    }

    /// <summary>
    /// Try to set the texture.
    /// </summary>
    /// <param name="id">The shader resource id of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    /// <returns>True if the texture is set successfully, otherwise false.</returns>
    public bool TrySetTexture(uint id, Texture2D texture)
    {
        return _parameters.TrySetTexture(id, texture);
    }

    /// <summary>
    /// Set the texture.
    /// </summary>
    /// <param name="name">The shader resource name of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(string name, Texture2D texture)
    {
        _parameters.SetTexture(name, texture);
    }

    /// <summary>
    /// Set the texture.
    /// </summary>
    /// <param name="id">The shader resource id of the texture.</param>
    /// <param name="texture">The texture to set.</param>
    public void SetTexture(uint id, Texture2D texture)
    {
        _parameters.SetTexture(id, texture);
    }


    #endregion

    #region Set render texture

    /// <summary>
    /// Try to set the render texture.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the render texture.</param>
    /// <returns>True if the render texture is set successfully, otherwise false.</returns>
    public bool TrySetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        return _parameters.TrySetRenderTexture(name, renderTexture, renderTextureIndex);
    }

    /// <summary>
    /// Try to set the render texture.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the render texture.</param>
    /// <returns>True if the render texture is set successfully, otherwise false.</returns>
    public bool TrySetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        return _parameters.TrySetRenderTexture(id, renderTexture, renderTextureIndex);
    }

    /// <summary>
    /// Set the render texture.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the render texture.</param>
    public void SetRenderTexture(string name, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        _parameters.SetRenderTexture(name, renderTexture, renderTextureIndex);
    }

    /// <summary>
    /// Set the render texture.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <param name="renderTextureIndex">The index of the render texture.</param>
    public void SetRenderTexture(uint id, RenderTexture renderTexture, int renderTextureIndex = 0)
    {
        _parameters.SetRenderTexture(id, renderTexture, renderTextureIndex);
    }

    /// <summary>
    /// Try to set the render texture depth.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>True if the render texture is set successfully, otherwise false.</returns>
    public bool TrySetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        return _parameters.TrySetRenderTextureDepth(name, renderTexture);
    }

    /// <summary>
    /// Try to set the render texture depth.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    /// <returns>True if the render texture is set successfully, otherwise false.</returns>
    public bool TrySetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        return _parameters.TrySetRenderTextureDepth(id, renderTexture);
    }

    /// <summary>
    /// Set the render texture depth.
    /// </summary>
    /// <param name="name">The shader resource name of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(string name, RenderTexture renderTexture)
    {
        _parameters.SetRenderTextureDepth(name, renderTexture);
    }

    /// <summary>
    /// Set the render texture depth.
    /// </summary>
    /// <param name="id">The shader resource id of the render texture.</param>
    /// <param name="renderTexture">The render texture to set.</param>
    public void SetRenderTextureDepth(uint id, RenderTexture renderTexture)
    {
        _parameters.SetRenderTextureDepth(id, renderTexture);
    }

    #endregion

    protected void UpdateSlotResources(ShaderReflectionInfo reflectionInfo)
    {
        for (uint i = 0; i < reflectionInfo.BindGroups.Count; i++)
        {

            BindGroupLayout bindGroupLayout = reflectionInfo.BindGroups[(int)i];
            if (UtilsMaterial.IsUniformBufferGroup(bindGroupLayout.Bindings))
            {
                BindGroupEntryInfo info = bindGroupLayout.Bindings[0];
                if (!_parameters.TryGetBuffer(i, out GraphicsBuffer? buffer))
                {
                    
                    buffer = _system.CreateGraphicsBuffer(
                        info.Size,
                        $"Material_{Name}_Buffer_{info.Entry.Name}"
                    );
                    _parameters.SetBuffer(i, buffer);
                    _managedResources.Add(buffer);
                }
                else if (buffer.Size < info.Size)
                {
                    buffer.Dispose();
                    buffer = _system.CreateGraphicsBuffer(
                        info.Size,
                        $"Material_{Name}_Buffer_{info.Entry.Name}"
                    );
                    _parameters.SetBuffer(i, buffer);
                    _managedResources.Add(buffer);
                }
            }
            else if (UtilsMaterial.IsTextureSamplerGroup(bindGroupLayout.Bindings))
            {
                if (!_parameters.TryGetTexture(i, out Texture2D? _) &&
                    !_parameters.TryGetRenderTexture(i, out RenderTexture? _))
                {
                    _parameters.SetTexture(i, _system.TextureWhite);
                }
            }
            else
            {
                //do nothing
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (AutoDisposable resource in _managedResources)
            {
                resource.Dispose();
            }
        }
    }
}
