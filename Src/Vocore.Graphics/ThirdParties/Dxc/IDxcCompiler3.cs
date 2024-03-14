// Copyright © Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.
// Implementation based on https://github.com/tgjones/DotNetDxc

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vocore.Graphics;

namespace Vortice.Dxc;

public partial class IDxcCompiler3
{
    public unsafe IDxcResult Compile(string source, string[] arguments, IDxcIncludeHandler includeHandler)
    {
        Compile(source, arguments, includeHandler, out IDxcResult? result).CheckError();
        return result!;
    }

    public unsafe Result Compile<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string source, string[] arguments, IDxcIncludeHandler includeHandler, out T? result) where T : ComObject
    {
        IntPtr shaderSourcePtr = Marshal.StringToHGlobalAnsi(source);

        // this will be wchar_t**, 2 bytes on windows, 4 bytes on unix
        IntPtr pArgs = IntPtr.Zero;
        int argsCount = arguments.Length;

        Utf32StringArray? utf32Array = null;
        Utf16StringArray? utf16Array = null;

        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            utf16Array = new Utf16StringArray(arguments);
            pArgs = utf16Array.Value.Pointer;
        }
        else
        {
            utf32Array = new Utf32StringArray(arguments);
            pArgs = utf32Array.Value.Pointer;
        }
        

        DxcBuffer buffer = new()
        {
            Ptr = shaderSourcePtr,
            Size = source.Length,
            Encoding = Dxc.DXC_CP_ACP
        };

        try
        {

            Result hr = Compile(ref buffer, pArgs, argsCount, includeHandler, typeof(T).GUID, out IntPtr nativePtr);
            if (hr.Failure)
            {
                result = default;
                return hr;
            }

            result = MarshallingHelpers.FromPointer<T>(nativePtr);
            return hr;
        }
        finally
        {
            if (shaderSourcePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(shaderSourcePtr);
            }

            utf32Array?.Dispose();
            utf16Array?.Dispose();
        }
    }

    public T Disassemble<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(in DxcBuffer buffer) where T : IDxcResult
    {
        Disassemble(buffer, out T? result).CheckError();
        return result!;
    }

    public unsafe Result Disassemble<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(DxcBuffer buffer, out T? result) where T : IDxcResult
    {
        Result hr = Disassemble(ref buffer, typeof(T).GUID, out IntPtr nativePtr);
        if (hr.Failure)
        {
            result = default;
            return hr;
        }

        result = MarshallingHelpers.FromPointer<T>(nativePtr);
        return hr;
    }

    public T Disassemble<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string source) where T : IDxcResult
    {
        Disassemble(source, out T? result).CheckError();
        return result!;
    }

    public unsafe Result Disassemble<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string source, out T? result) where T : IDxcResult
    {
        IntPtr shaderSourcePtr = Marshal.StringToHGlobalAnsi(source);

        DxcBuffer buffer = new()
        {
            Ptr = shaderSourcePtr,
            Size = source.Length,
            Encoding = Dxc.DXC_CP_ACP
        };

        try
        {
            Result hr = Disassemble(ref buffer, typeof(T).GUID, out IntPtr nativePtr);
            if (hr.Failure)
            {
                result = default;
                return hr;
            }

            result = MarshallingHelpers.FromPointer<T>(nativePtr);
            return hr;
        }
        finally
        {
            if (shaderSourcePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(shaderSourcePtr);
            }
        }
    }
}
