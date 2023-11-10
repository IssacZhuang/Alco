using System;
using NativeLibraryLoader;
using System.Runtime.InteropServices;

using NativeLibrary = NativeLibraryLoader.NativeLibrary;

namespace Vocore.ShaderConductor
{
    public static unsafe class ShaderConductorNative
    {
        private static readonly NativeLibrary s_bibShaderConductor = LoadNativeLib();

        public static NativeLibrary LoadNativeLib()
        {
            string[] libNames;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                libNames = new[] { "ShaderConductorWrapper.dll" };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                libNames = new[]
                {
                    "libShaderConductorWrapper.so",
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                libNames = new[]
                {
                    "libShaderConductorWrapper.dylib"
                };
            }
            else
            {
                libNames = new[] { "ShaderConductorWrapper.dll" };
            }

            return new NativeLibrary(libNames);
        }

        public static T LoadFunction<T>(string name)
        {
            try
            {
                return s_bibShaderConductor.LoadFunction<T>(name);
            }
            catch
            {
                Console.WriteLine(
                    $"Unable to load SDL2 function \"{name}\". " +
                    $"Attempting to call this function will cause an exception to be thrown.");
                return default(T);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Native_Compile(Native_SourceDescription* source, Native_OptionsDescription* optionsDesc, Native_TargetDescription* target, Native_ResultDescription* result);
        private static readonly Native_Compile s_compile = LoadFunction<Native_Compile>("Compile");
        internal static unsafe void Compile(Native_SourceDescription* source, Native_OptionsDescription* optionsDesc, Native_TargetDescription* target, Native_ResultDescription* result)
        {
            s_compile(source, optionsDesc, target, result);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Native_Disassemble(Native_DisassembleDescription* source, Native_ResultDescription* result);
        private static readonly Native_Disassemble s_disassemble = LoadFunction<Native_Disassemble>("Disassemble");
        internal static unsafe void Disassemble(Native_DisassembleDescription* source, Native_ResultDescription* result)
        {
            s_disassemble(source, result);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Native_DestroyShaderConductorBlob(void* blob);
        private static readonly Native_DestroyShaderConductorBlob s_destroyShaderConductorBlob = LoadFunction<Native_DestroyShaderConductorBlob>("DestroyShaderConductorBlob");
        internal static unsafe void DestroyShaderConductorBlob(void* blob)
        {
            s_destroyShaderConductorBlob(blob);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void* Native_CreateShaderConductorBlob(void* data, int size);
        private static readonly Native_CreateShaderConductorBlob s_createShaderConductorBlob = LoadFunction<Native_CreateShaderConductorBlob>("CreateShaderConductorBlob");
        internal static unsafe void* CreateShaderConductorBlob(void* data, int size)
        {
            return s_createShaderConductorBlob(data, size);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void* Native_GetShaderConductorBlobData(void* blob);
        private static readonly Native_GetShaderConductorBlobData s_getShaderConductorBlobData = LoadFunction<Native_GetShaderConductorBlobData>("GetShaderConductorBlobData");
        internal static unsafe void* GetShaderConductorBlobData(void* blob)
        {
            return s_getShaderConductorBlobData(blob);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Native_GetShaderConductorBlobSize(void* blob);
        private static readonly Native_GetShaderConductorBlobSize s_getShaderConductorBlobSize = LoadFunction<Native_GetShaderConductorBlobSize>("GetShaderConductorBlobSize");
        internal static unsafe int GetShaderConductorBlobSize(void* blob)
        {
            return s_getShaderConductorBlobSize(blob);
        }
    }

    internal enum Native_ShaderStage : int
    {
        VertexShader,
        PixelShader,
        GeometryShader,
        HullShader,
        DomainShader,
        ComputeShader,

        NumShaderStages,
    };

    internal enum Native_ShadingLanguage : int
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
    internal unsafe struct Native_SourceDescription
    {
        public byte* source;
        public byte* entryPoint;
        public Native_ShaderStage stage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Native_ShaderModel
    {
        public int major;
        public int minor;

        public Native_ShaderModel(int major, int minor)
        {
            this.major = major;
            this.minor = minor;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Native_OptionsDescription
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

        public Native_ShaderModel shaderModel;

        public int shiftAllTexturesBindings;
        public int shiftAllSamplersBindings;
        public int shiftAllCBuffersBindings;
        public int shiftAllUABuffersBindings;

        public static Native_OptionsDescription Default
        {
            get
            {
                var defaultInstance = new Native_OptionsDescription
                {
                    packMatricesInRowMajor = false,
                    enable16bitTypes = false,
                    enableDebugInfo = false,
                    disableOptimizations = false,
                    optimizationLevel = 3,
                    shaderModel = new Native_ShaderModel(6, 0)
                };

                return defaultInstance;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Native_TargetDescription
    {
        public Native_ShadingLanguage language;
        public byte* version;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Native_ResultDescription
    {
        public void* target;
        public bool isText;

        public void* errorWarningMsg;
        public bool hasError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Native_DisassembleDescription
    {
        public Native_ShadingLanguage language;
        public byte* binary;
        public int binarySize;
    }


}


// Content form Native.h from ShaderConductor wrapper:

/*
 * ShaderConductor
 *
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License.
 *
 * MIT License
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this
 * software and associated documentation files (the "Software"), to deal in the Software
 * without restriction, including without limitation the rights to use, copy, modify, merge,
 * publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
 * to whom the Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
 * INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
 * PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
 * FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
 * OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

/*

#pragma once

#include <ShaderConductor/ShaderConductor.hpp>

using namespace ShaderConductor;

struct ShaderConductorBlob;

struct SourceDescription
{
    const char* source;
    const char* entryPoint;
    ShaderStage stage;
};

struct ShaderModel
{
    int major;
    int minor;
};

struct OptionsDescription
{
    bool packMatricesInRowMajor = true; // Experimental: Decide how a matrix get packed
    bool enable16bitTypes = false;      // Enable 16-bit types, such as half, uint16_t. Requires shader model 6.2+
    bool enableDebugInfo = false;       // Embed debug info into the binary
    bool disableOptimizations = false;  // Force to turn off optimizations. Ignore optimizationLevel below.
    int optimizationLevel = 3;          // 0 to 3, no optimization to most optimization

    ShaderModel shaderModel = { 6, 0 };

    int shiftAllTexturesBindings;
    int shiftAllSamplersBindings;
    int shiftAllCBuffersBindings;
    int shiftAllUABuffersBindings;
};

struct TargetDescription
{
    ShadingLanguage shadingLanguage;
    const char* version;
};

struct ResultDescription
{
    ShaderConductorBlob* target;
    bool isText;

    ShaderConductorBlob* errorWarningMsg;
    bool hasError;
};

struct DisassembleDescription
{
    ShadingLanguage language;
    char* binary;
    int binarySize;
};

#define DLLEXPORT extern "C" __declspec(dllexport)

DLLEXPORT void Compile(SourceDescription* source, OptionsDescription* optionsDesc, TargetDescription* target, ResultDescription* result);
DLLEXPORT void Disassemble(DisassembleDescription* source, ResultDescription* result);

DLLEXPORT ShaderConductorBlob* CreateShaderConductorBlob(const void* data, int size);
DLLEXPORT void DestroyShaderConductorBlob(ShaderConductorBlob* blob);
DLLEXPORT const void* GetShaderConductorBlobData(ShaderConductorBlob * blob);
DLLEXPORT int GetShaderConductorBlobSize(ShaderConductorBlob* blob);

*/