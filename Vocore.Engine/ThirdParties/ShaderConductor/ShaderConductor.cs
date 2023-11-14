/// source code from MonoGame

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Vocore.ShaderCross
{
    public static class ShaderConductor
    {
        [DllImport("ShaderConductorWrapper", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Compile([In] ref SourceDesc source, [In] ref OptionsDesc options, [In] ref TargetDesc target, out ResultDesc result);

        [DllImport("ShaderConductorWrapper", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Disassemble([In] ref DisassembleDesc source, out ResultDesc result);

        [DllImport("ShaderConductorWrapper", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CreateShaderConductorBlob(IntPtr data, int size);

        [DllImport("ShaderConductorWrapper", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DestroyShaderConductorBlob(IntPtr blob);

        [DllImport("ShaderConductorWrapper", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetShaderConductorBlobData(IntPtr blob);

        [DllImport("ShaderConductorWrapper", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetShaderConductorBlobSize(IntPtr blob);

        public enum ShaderStage
        {
            VertexShader,
            PixelShader,
            GeometryShader,
            HullShader,
            DomainShader,
            ComputeShader,

            NumShaderStages,
        };

        public enum ShadingLanguage
        {
            Dxil = 0,
            SpirV,

            Hlsl,
            Glsl,
            Essl,
            Msl_macOS,
            Msl_iOS,

            NumShadingLanguages,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MacroDefine
        {
            public string name;
            public string value;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct SourceDesc
        {
            public string source;
            public string entryPoint;
            public ShaderStage stage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ShaderModel
        {
            public int major;
            public int minor;

            public ShaderModel(int major, int minor)
            {
                this.major = major;
                this.minor = minor;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OptionsDesc
        {
            [MarshalAs(UnmanagedType.I1)]
            public bool packMatricesInRowMajor; // Experimental: Decide how a matrix get packed
            [MarshalAs(UnmanagedType.I1)]
            public bool enable16bitTypes; // Enable 16-bit types, such as half, uint16_t. Requires shader model 6.2+
            [MarshalAs(UnmanagedType.I1)]
            public bool enableDebugInfo; // Embed debug info into the binary
            [MarshalAs(UnmanagedType.I1)]
            public bool disableOptimizations; // Force to turn off optimizations. Ignore optimizationLevel below.
            public int optimizationLevel; // 0 to 3, no optimization to most optimization

            public ShaderModel shaderModel;

            public int shiftAllTexturesBindings;
            public int shiftAllSamplersBindings;
            public int shiftAllCBuffersBindings;
            public int shiftAllUABuffersBindings;

            public static OptionsDesc Default
            {
                get
                {
                    var defaultInstance = new OptionsDesc();

                    defaultInstance.packMatricesInRowMajor = false;
                    defaultInstance.enable16bitTypes = false;
                    defaultInstance.enableDebugInfo = false;
                    defaultInstance.disableOptimizations = false;
                    defaultInstance.optimizationLevel = 3;
                    defaultInstance.shaderModel = new ShaderModel(6, 0);

                    return defaultInstance;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TargetDesc
        {
            public ShadingLanguage language;
            public string? version;
            public bool asModule;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResultDesc
        {
            public IntPtr target;
            public bool isText;
            public IntPtr errorWarningMsg;
            public bool hasError;
            public IntPtr reflection;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisassembleDesc
        {
            public ShadingLanguage language;
            public IntPtr binary;
            public int binarySize;
        }
    }
}
