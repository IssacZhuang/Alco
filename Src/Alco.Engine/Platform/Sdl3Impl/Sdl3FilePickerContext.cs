using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SDL3;
using static Alco.UtilsMemory;

namespace Alco.Engine;

public class DialogFileFilter
{
    public string Name { get; }
    public string Pattern { get; }

    public DialogFileFilter(string name, string pattern)
    {
        Name = name;
        Pattern = pattern;
    }
}


internal unsafe sealed class Sdl3FilePickerContext
{
    public GCHandle Handle;
    public TaskCompletionSource<string[]> Completion = null!;

    public SDL_DialogFileFilter* NativeFilters;
    public int NativeFiltersCount;
    public List<NativeUtf8String> FilterNames = new();
    public List<NativeUtf8String> FilterPatterns = new();

    public static Sdl3FilePickerContext Create(ReadOnlySpan<DialogFileFilter> filters, TaskCompletionSource<string[]> completion)
    {
        Sdl3FilePickerContext context = new Sdl3FilePickerContext
        {
            Completion = completion,
            NativeFiltersCount = filters.Length
        };

        if (filters.Length > 0)
        {
            context.NativeFilters = Alloc<SDL_DialogFileFilter>(filters.Length);
            for (int i = 0; i < filters.Length; i++)
            {
                NativeUtf8String nativeName = new NativeUtf8String(filters[i].Name);
                NativeUtf8String nativePattern = new NativeUtf8String(filters[i].Pattern);
                context.FilterNames.Add(nativeName);
                context.FilterPatterns.Add(nativePattern);
                context.NativeFilters[i].name = nativeName.UnsafePointer;
                context.NativeFilters[i].pattern = nativePattern.UnsafePointer;
            }
        }

        context.Handle = GCHandle.Alloc(context, GCHandleType.Normal);
        return context;
    }

    public void Cleanup()
    {
        for (int i = 0; i < FilterNames.Count; i++)
        {
            FilterNames[i].Dispose();
            FilterPatterns[i].Dispose();
        }
        FilterNames.Clear();
        FilterPatterns.Clear();

        if (NativeFilters != null)
        {
            Free(NativeFilters);
            NativeFilters = null;
            NativeFiltersCount = 0;
        }

        if (Handle.IsAllocated)
        {
            Handle.Free();
        }
    }
}

