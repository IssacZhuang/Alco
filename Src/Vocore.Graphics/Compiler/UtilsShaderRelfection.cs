using Silk.NET.SPIRV.Reflect;

namespace Vocore.Graphics;

public static class UtilsShaderRelfection
{
    private static readonly Reflect API = Reflect.GetApi();

    public unsafe static ShaderReflectionInfo GetReflectionInfo(byte[] spirv)
    {
        ReflectShaderModule module = new ReflectShaderModule();
        fixed (byte* ptr = spirv)
        {
            Result result = API.CreateShaderModule((nuint)spirv.Length, ptr, &module);
            if (result != Result.Success)
            {
                throw new ShaderCompilationException("Failed to create shader module");
            }
        }


        //TODO: implement reflection info
        return new ShaderReflectionInfo
        {
            
        };
    }

    // resource binding reflection

    private unsafe static BindGroupEntry ConvertResourceBinding(DescriptorBinding input, ShaderStage stage)
    {
        BindingType type = UtilsRelfectType.ConvertBindingType(input.DescriptorType);

        TextureBindingInfo? textureBindingInfo = null;
        StorageTextureBindingInfo? storageTextureBindingInfo = null;

        switch (type)
        {
            case BindingType.Texture:
                textureBindingInfo = new TextureBindingInfo(UtilsRelfectType.ConvertTextureViewDimension(input.Image));
                break;
            case BindingType.StorageTexture:
                storageTextureBindingInfo = new StorageTextureBindingInfo(
                    AccessMode.Write,
                    UtilsRelfectType.ConvertTextureViewDimension(input.Image),
                    UtilsRelfectType.ConvertImageFormat(input.Image.ImageFormat)
                    );
                break;
        }

        return new BindGroupEntry(
            input.Binding,
            stage,
            type,
            textureBindingInfo,
            storageTextureBindingInfo
        );
    }

    // vertex layout reflection

    private unsafe static VertexElement ConvertVertexElement(InterfaceVariable input)
    {
        return new VertexElement
        {
            Name = UtilsInterop.ReadString(input.Name),
            Format = UtilsRelfectType.ConvertFormat(input.Format),
            Offset = input.Location
        };
    }

    private unsafe static VertexElement[] GetVertexElements(InterfaceVariable** inputs, uint count, out uint stride)
    {
        stride = 0;
        VertexElement[] elements = new VertexElement[count];
        for (int i = 0; i < count; i++)
        {
            InterfaceVariable* input = inputs[i];
            elements[i] = ConvertVertexElement(*input);
            stride += GetNumericSize(input->Numeric);
        }

        return elements;
    }

    private unsafe static VertexInputLayout GetVertexInputLayout(ReflectShaderModule shaderModule)
    {
        VertexElement[] elements = GetVertexElements(shaderModule.InputVariables, shaderModule.InputVariableCount, out uint stride);
        return new VertexInputLayout(elements, stride, VertexStepMode.Vertex);
    }

    private static uint GetNumericSize(NumericTraits num)
    {
        return num.Scalar.Width / 8 * num.Vector.ComponentCount;
    }

}