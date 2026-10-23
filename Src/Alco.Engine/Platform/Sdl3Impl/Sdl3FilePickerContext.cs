using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SDL3;
using static Alco.MemoryUtility;

namespace Alco.Engine;

/// <summary>
/// Describes a file type filter for a file picker dialog.
/// </summary>
public class DialogFileFilter
{
    /// <summary>
    /// Gets the display name of the filter.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the file matching pattern of the filter.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="DialogFileFilter"/>.
    /// </summary>
    /// <param name="name">Display name of the filter.</param>
    /// <param name="pattern">File matching pattern, for example "*.png".</param>
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

