using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Particles;

/// <summary>
/// A scalar range sampled uniformly at spawn time (on the GPU): every spawned
/// particle draws a value in [<see cref="Min"/>, <see cref="Max"/>].
/// Serialized as <c>{ "min": 1, "max": 2 }</c> or a plain number (min = max).
/// </summary>
public struct ParticleRange
{
    /// <summary>The inclusive lower bound of the sampled range.</summary>
    public float Min { get; set; }

    /// <summary>The inclusive upper bound of the sampled range.</summary>
    public float Max { get; set; }

    /// <summary>Creates a range that always samples <paramref name="value"/>.</summary>
    public ParticleRange(float value)
    {
        Min = value;
        Max = value;
    }

    /// <summary>Creates a range sampling uniformly between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public ParticleRange(float min, float max)
    {
        Min = min;
        Max = max;
    }
}

/// <summary>
/// A <see cref="Vector2"/> range sampled component-wise at spawn time (on the GPU).
/// Serialized as <c>{ "min": {...}, "max": {...} }</c>.
/// </summary>
public struct ParticleVector2Range
{
    /// <summary>The inclusive lower bound of the sampled range.</summary>
    public Vector2 Min { get; set; }

    /// <summary>The inclusive upper bound of the sampled range.</summary>
    public Vector2 Max { get; set; }

    /// <summary>Creates a range that always samples <paramref name="value"/>.</summary>
    public ParticleVector2Range(Vector2 value)
    {
        Min = value;
        Max = value;
    }
}

/// <summary>
/// JSON converter for <see cref="ParticleRange"/>: accepts a number (constant
/// value) or a <c>{ "min": .., "max": .. }</c> object.
/// </summary>
public class JsonConverterParticleRange : JsonConverter<ParticleRange>
{
    /// <inheritdoc />
    public override ParticleRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return new ParticleRange(reader.GetSingle());
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float min = 0f, max = 0f;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected 'min' or 'max' in a particle range value.");
                }
                string name = reader.GetString() ?? string.Empty;
                reader.Read();
                if (name.Equals("min", StringComparison.OrdinalIgnoreCase)) min = reader.GetSingle();
                else if (name.Equals("max", StringComparison.OrdinalIgnoreCase)) max = reader.GetSingle();
                else throw new JsonException($"Unknown particle range member '{name}'.");
            }
            return new ParticleRange(min, max);
        }
        throw new JsonException("Expected a number or a { min, max } object for a particle range value.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ParticleRange value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("min", value.Min);
        writer.WriteNumber("max", value.Max);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter for <see cref="Vector2"/> in particle assets: a number
/// (broadcast), a hex color string, or a component object <c>{"x": 1, "y": 2}</c> —
/// the same shapes the material vector converters accept.
/// </summary>
public class JsonConverterParticleVector2 : JsonConverter<Vector2>
{
    /// <inheritdoc />
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            float value = reader.GetSingle();
            return new Vector2(value);
        }
        float x = 0f, y = 0f;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a number or a { x, y } object for a vector2 value.");
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a component name when reading a vector2 value.");
            }
            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            if (name.Equals("x", StringComparison.OrdinalIgnoreCase)) x = reader.GetSingle();
            else if (name.Equals("y", StringComparison.OrdinalIgnoreCase)) y = reader.GetSingle();
            else throw new JsonException($"Unknown component '{name}' of a vector2 value.");
        }
        return new Vector2(x, y);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON converter for <see cref="ColorFloat"/> in particle assets: a number
/// (broadcast), a hex color string (<c>"#RRGGBB"</c> / <c>"#RRGGBBAA"</c>), or a
/// component object (<c>{"r": 1, "g": 0.5, ...}</c>, vector-style names tolerated) —
/// the same shapes the material vector converters accept.
/// </summary>
public class JsonConverterParticleColor : JsonConverter<ColorFloat>
{
    /// <inheritdoc />
    public override ColorFloat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            float value = reader.GetSingle();
            return new ColorFloat(value, value, value, value);
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            string? hex = reader.GetString();
            if (hex != null && ColorFloat.TryParse(hex, out ColorFloat color))
            {
                return color;
            }
            throw new JsonException($"Invalid hex color string '{hex}' for a particle color value.");
        }
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a number, a hex color string or a component object for a particle color value.");
        }
        ColorFloat result = default;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a component name when reading a particle color value.");
            }
            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            float component = reader.GetSingle();
            if (name.Equals("r", StringComparison.OrdinalIgnoreCase) || name.Equals("x", StringComparison.OrdinalIgnoreCase)) result.R = component;
            else if (name.Equals("g", StringComparison.OrdinalIgnoreCase) || name.Equals("y", StringComparison.OrdinalIgnoreCase)) result.G = component;
            else if (name.Equals("b", StringComparison.OrdinalIgnoreCase) || name.Equals("z", StringComparison.OrdinalIgnoreCase)) result.B = component;
            else if (name.Equals("a", StringComparison.OrdinalIgnoreCase) || name.Equals("w", StringComparison.OrdinalIgnoreCase)) result.A = component;
            else throw new JsonException($"Unknown component '{name}' of a particle color value.");
        }
        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ColorFloat value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("r", value.R);
        writer.WriteNumber("g", value.G);
        writer.WriteNumber("b", value.B);
        writer.WriteNumber("a", value.A);
        writer.WriteEndObject();
    }
}

/// <summary>The emission shape of a 2D particle group.</summary>
public enum ParticleShape2DType
{
    /// <summary>All particles spawn at the emitter origin.</summary>
    Point,
    /// <summary>Particles spawn uniformly inside a circle (radius and optional hollow inner radius).</summary>
    Circle,
    /// <summary>Particles spawn uniformly inside an axis-aligned box of the given extents.</summary>
    Box,
}

/// <summary>The emission shape of a 3D particle group.</summary>
public enum ParticleShape3DType
{
    /// <summary>All particles spawn at the emitter origin.</summary>
    Point,
    /// <summary>Particles spawn uniformly inside a sphere (radius and optional hollow inner radius).</summary>
    Sphere,
    /// <summary>Particles spawn uniformly inside the upper hemisphere of a sphere.</summary>
    Hemisphere,
    /// <summary>Particles spawn uniformly inside an axis-aligned box of the given extents.</summary>
    Box,
}

/// <summary>How the initial velocity direction of a particle is chosen.</summary>
public enum ParticleDirectionMode
{
    /// <summary>The authored base direction, randomized inside the spread angle.</summary>
    Constant,
    /// <summary>Radial outward from the emitter center through the spawn position (falls back to the base direction at the center).</summary>
    Radial,
}

/// <summary>The space the particles of a group simulate in.</summary>
public enum ParticleSimulationSpace
{
    /// <summary>
    /// Particles spawn through the emitter transform and then live in world space:
    /// a moving emitter leaves a trail behind.
    /// </summary>
    World,
    /// <summary>
    /// Particles simulate in emitter-local space; the whole effect follows the
    /// emitter transform (applied at draw time).
    /// </summary>
    Local,
}

/// <summary>
/// A one-shot burst of particles at a point in the emitter's timeline. Bursts
/// re-trigger on every loop of a looping emitter.
/// </summary>
public class ParticleBurst
{
    /// <summary>The time in seconds on the emitter timeline at which the burst fires.</summary>
    public float Time { get; set; }

    /// <summary>The minimum number of particles the burst spawns.</summary>
    public int CountMin { get; set; } = 10;

    /// <summary>The maximum number of particles the burst spawns.</summary>
    public int CountMax { get; set; } = 10;
}

/// <summary>The emission shape parameters of a 2D particle group.</summary>
public class ParticleShape2D
{
    /// <summary>The shape kind; selects which of the other fields apply.</summary>
    public ParticleShape2DType Type { get; set; } = ParticleShape2DType.Point;

    /// <summary>The circle radius in world units (<see cref="ParticleShape2DType.Circle"/>).</summary>
    public float Radius { get; set; } = 10f;

    /// <summary>
    /// The hollow inner radius as a fraction of <see cref="Radius"/> in [0, 1]:
    /// 0 fills the whole shape, 1 spawns on the rim only.
    /// </summary>
    public float InnerRadius { get; set; }

    /// <summary>The box half extents in world units (<see cref="ParticleShape2DType.Box"/>).</summary>
    public Vector2 Extents { get; set; } = new Vector2(10f);
}

/// <summary>The emission shape parameters of a 3D particle group.</summary>
public class ParticleShape3D
{
    /// <summary>The shape kind; selects which of the other fields apply.</summary>
    public ParticleShape3DType Type { get; set; } = ParticleShape3DType.Point;

    /// <summary>The sphere/hemisphere radius in world units.</summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>
    /// The hollow inner radius as a fraction of <see cref="Radius"/> in [0, 1]:
    /// 0 fills the whole shape, 1 spawns on the shell only.
    /// </summary>
    public float InnerRadius { get; set; }

    /// <summary>The box half extents in world units (<see cref="ParticleShape3DType.Box"/>).</summary>
    public Vector3 Extents { get; set; } = new Vector3(0.5f);
}

/// <summary>
/// One key of a color-over-life gradient (<see cref="ParticleGroupAsset.ColorGradient"/>):
/// the color at a normalized particle age. Keys may be authored out of order and
/// outside [0, 1]; the bake (<see cref="ParticleOverLifeBake"/>) sorts and clamps them.
/// </summary>
public class ParticleColorKey
{
    /// <summary>The normalized particle age of the key in [0, 1].</summary>
    public float Time { get; set; }

    /// <summary>The gradient color at <see cref="Time"/> (accepts every color shape).</summary>
    public ColorFloat Color { get; set; } = ColorFloat.White;
}

/// <summary>
/// One key of a scalar-over-life curve (<see cref="ParticleGroupAsset.SizeCurve"/>):
/// the multiplier at a normalized particle age; see <see cref="ParticleColorKey"/>
/// for the key ordering/clamping rules.
/// </summary>
public class ParticleScalarKey
{
    /// <summary>The normalized particle age of the key in [0, 1].</summary>
    public float Time { get; set; }

    /// <summary>The curve value at <see cref="Time"/>; may exceed 1 (e.g. growth).</summary>
    public float Value { get; set; } = 1f;
}

/// <summary>Flipbook (sprite-sheet) animation parameters of a particle group.</summary>
public class ParticleFlipbook
{
    /// <summary>The number of rows in the sprite sheet.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>The number of columns in the sprite sheet.</summary>
    public int Cols { get; set; } = 1;

    /// <summary>The playback rate in frames per second over the particle's age.</summary>
    public float Fps { get; set; } = 30f;

    /// <summary>Whether the animation loops instead of clamping to the last frame.</summary>
    public bool Loop { get; set; } = true;
}
