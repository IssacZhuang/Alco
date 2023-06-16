using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace Vocore
{
    public static class NativeCollectionExtension
    {
        public static unsafe void CopyToComputeBuffer<T>(this NativeBuffer<T> nativeBuffer, ComputeBuffer computeBuffer) where T : unmanaged
        {
            if (nativeBuffer.Stride * nativeBuffer.Count > computeBuffer.count * computeBuffer.stride) throw ExceptionCollection.CommandBufferNotEnoughSize(nativeBuffer.Stride * nativeBuffer.Count, computeBuffer.count * computeBuffer.stride);
            T* ptrNative = nativeBuffer.Ptr;
            T* ptrCompute = (T*)computeBuffer.GetNativeBufferPtr();

            for (int i = 0; i < nativeBuffer.Size; i++)
            {
                ptrCompute[i] = ptrNative[i];
            }
        }

        public static unsafe void CopyToNativeBuffer<T>(this ComputeBuffer computeBuffer, NativeBuffer<T> nativeBuffer) where T : unmanaged
        {
            if (nativeBuffer.Stride * nativeBuffer.Count > computeBuffer.count * computeBuffer.stride) throw ExceptionCollection.CommandBufferNotEnoughSize(nativeBuffer.Stride * nativeBuffer.Count, computeBuffer.count * computeBuffer.stride);
            T* ptrNative = nativeBuffer.Ptr;
            T* ptrCompute = (T*)computeBuffer.GetNativeBufferPtr();

            for (int i = 0; i < nativeBuffer.Size; i++)
            {
                ptrNative[i] = ptrCompute[i];
            }
        }
    }
}

