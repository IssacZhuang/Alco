using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.ImGUI;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    /// <summary>
    /// Display a color button with ColorFloat support.
    /// </summary>
    /// <param name="desc_id">The ID for the button</param>
    /// <param name="color">The ColorFloat color to display</param>
    /// <returns>True if the button was pressed</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorButton(string desc_id, ColorFloat color)
    {
        return ColorButton(desc_id, color.value);
    }

    /// <summary>
    /// Display a color button with ColorFloat support and custom flags/size.
    /// </summary>
    /// <param name="desc_id">The ID for the button</param>
    /// <param name="color">The ColorFloat color to display</param>
    /// <param name="flags">Color edit flags</param>
    /// <param name="size">Button size</param>
    /// <returns>True if the button was pressed</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorButton(string desc_id, ColorFloat color, ImGuiColorEditFlags flags, Vector2 size)
    {
        return ColorButton(desc_id, color.value, flags, size);
    }

    /// <summary>
    /// Display a 4-component color editor with ColorFloat support.
    /// </summary>
    /// <param name="label">The label for the color editor</param>
    /// <param name="color">The ColorFloat color to edit</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorEdit4(string label, ref ColorFloat color)
    {
        Vector4 vec = color.value;
        bool result = ColorEdit4(label, ref vec);
        if (result)
        {
            color.value = vec;
        }
        return result;
    }

    /// <summary>
    /// Display a 4-component color editor with ColorFloat support and custom flags.
    /// </summary>
    /// <param name="label">The label for the color editor</param>
    /// <param name="color">The ColorFloat color to edit</param>
    /// <param name="flags">Color edit flags</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorEdit4(string label, ref ColorFloat color, ImGuiColorEditFlags flags)
    {
        Vector4 vec = color.value;
        bool result = ColorEdit4(label, ref vec, flags);
        if (result)
        {
            color.value = vec;
        }
        return result;
    }

    /// <summary>
    /// Display a 3-component color editor with ColorFloat support (no alpha).
    /// </summary>
    /// <param name="label">The label for the color editor</param>
    /// <param name="color">The ColorFloat color to edit</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorEdit3(string label, ref ColorFloat color)
    {
        Vector3 vec = new Vector3(color.R, color.G, color.B);
        bool result = ColorEdit3(label, ref vec);
        if (result)
        {
            color.R = vec.X;
            color.G = vec.Y;
            color.B = vec.Z;
        }
        return result;
    }

    /// <summary>
    /// Display a 3-component color editor with ColorFloat support (no alpha) and custom flags.
    /// </summary>
    /// <param name="label">The label for the color editor</param>
    /// <param name="color">The ColorFloat color to edit</param>
    /// <param name="flags">Color edit flags</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorEdit3(string label, ref ColorFloat color, ImGuiColorEditFlags flags)
    {
        Vector3 vec = new Vector3(color.R, color.G, color.B);
        bool result = ColorEdit3(label, ref vec, flags);
        if (result)
        {
            color.R = vec.X;
            color.G = vec.Y;
            color.B = vec.Z;
        }
        return result;
    }

    /// <summary>
    /// Display a 4-component color picker with ColorFloat support.
    /// </summary>
    /// <param name="label">The label for the color picker</param>
    /// <param name="color">The ColorFloat color to pick</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorPicker4(string label, ref ColorFloat color)
    {
        Vector4 vec = color.value;
        bool result = ColorPicker4(label, ref vec);
        if (result)
        {
            color.value = vec;
        }
        return result;
    }

    /// <summary>
    /// Display a 4-component color picker with ColorFloat support and custom flags.
    /// </summary>
    /// <param name="label">The label for the color picker</param>
    /// <param name="color">The ColorFloat color to pick</param>
    /// <param name="flags">Color edit flags</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorPicker4(string label, ref ColorFloat color, ImGuiColorEditFlags flags)
    {
        Vector4 vec = color.value;
        bool result = ColorPicker4(label, ref vec, flags);
        if (result)
        {
            color.value = vec;
        }
        return result;
    }

    /// <summary>
    /// Display a 3-component color picker with ColorFloat support (no alpha).
    /// </summary>
    /// <param name="label">The label for the color picker</param>
    /// <param name="color">The ColorFloat color to pick</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorPicker3(string label, ref ColorFloat color)
    {
        Vector3 vec = new Vector3(color.R, color.G, color.B);
        bool result = ColorPicker3(label, ref vec);
        if (result)
        {
            color.R = vec.X;
            color.G = vec.Y;
            color.B = vec.Z;
        }
        return result;
    }

    /// <summary>
    /// Display a 3-component color picker with ColorFloat support (no alpha) and custom flags.
    /// </summary>
    /// <param name="label">The label for the color picker</param>
    /// <param name="color">The ColorFloat color to pick</param>
    /// <param name="flags">Color edit flags</param>
    /// <returns>True if the color was modified</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ColorPicker3(string label, ref ColorFloat color, ImGuiColorEditFlags flags)
    {
        Vector3 vec = new Vector3(color.R, color.G, color.B);
        bool result = ColorPicker3(label, ref vec, flags);
        if (result)
        {
            color.R = vec.X;
            color.G = vec.Y;
            color.B = vec.Z;
        }
        return result;
    }
}