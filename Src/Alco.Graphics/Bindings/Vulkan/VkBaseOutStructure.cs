// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

using System.Runtime.InteropServices;

namespace Vulkan;

[StructLayout(LayoutKind.Sequential)]
public unsafe partial struct VkBaseOutStructure : IStructureType
{
    public VkStructureType sType;
    public VkBaseOutStructure* pNext;

    /// <inheritdoc />
    readonly VkStructureType IStructureType.sType => sType;
}

