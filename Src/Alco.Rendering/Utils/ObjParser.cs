using System.Buffers;
using System.Numerics;

namespace Alco.Rendering.Utils;

/// <summary>
/// Parser for Wavefront OBJ mesh files.
/// Converts OBJ geometry data into vertex and index buffers suitable for GPU rendering.
/// </summary>
public sealed class ObjParser
{
    private static ReadOnlySpan<byte> KeywordV => "v"u8;
    private static ReadOnlySpan<byte> KeywordVt => "vt"u8;
    private static ReadOnlySpan<byte> KeywordVn => "vn"u8;
    private static ReadOnlySpan<byte> KeywordF => "f"u8;

    private readonly List<Vector3> _positions = [];
    private readonly List<Vector2> _uvs = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<VertexPositionNormalTexture> _vertices = [];
    private readonly List<uint> _indices = [];
    private readonly Dictionary<VertexKey, int> _vertexMap = [];

    private int[]? _tempFaceVertices;
    private int[]? _tempFaceUVs;
    private int[]? _tempFaceNormals;

    /// <summary>
    /// Parses the OBJ file data and returns the resulting mesh data.
    /// </summary>
    /// <param name="data">The raw OBJ file data as a byte span.</param>
    /// <returns>The parsed mesh data containing vertices and indices.</returns>
    public ObjParseResult Parse(ReadOnlySpan<byte> data)
    {
        Reset();

        var reader = new ObjLineReader(data);

        while (reader.ReadLine(out var line))
        {
            if (line.IsEmpty || line[0] == '#')
                continue;

            var keyword = ReadKeyword(ref line);

            if (keyword.SequenceEqual(KeywordV))
                ParsePosition(line);
            else if (keyword.SequenceEqual(KeywordVt))
                ParseUV(line);
            else if (keyword.SequenceEqual(KeywordVn))
                ParseNormal(line);
            else if (keyword.SequenceEqual(KeywordF))
                ParseFace(line);
        }

        return new ObjParseResult(
            _vertices.ToArray(),
            _indices.ToArray());
    }

    private void Reset()
    {
        _positions.Clear();
        _uvs.Clear();
        _normals.Clear();
        _vertices.Clear();
        _indices.Clear();
        _vertexMap.Clear();
    }

    private static ReadOnlySpan<byte> ReadKeyword(ref ReadOnlySpan<byte> line)
    {
        int spaceIndex = line.IndexOf((byte)' ');
        if (spaceIndex < 0)
        {
            var keyword = line;
            line = [];
            return keyword;
        }

        var result = line[..spaceIndex];
        line = line[(spaceIndex + 1)..];
        return result;
    }

    private void ParsePosition(ReadOnlySpan<byte> line)
    {
        var values = ParseFloats(line, 3);
        float x = values[0];
        float y = values[1];
        float z = -values[2];
        _positions.Add(new Vector3(x, y, z));
    }

    private void ParseUV(ReadOnlySpan<byte> line)
    {
        var values = ParseFloats(line, 2);
        _uvs.Add(new Vector2(values[0], values[1]));
    }

    private void ParseNormal(ReadOnlySpan<byte> line)
    {
        var values = ParseFloats(line, 3);
        float x = values[0];
        float y = values[1];
        float z = -values[2];
        _normals.Add(new Vector3(x, y, z));
    }

    private static float[] ParseFloats(ReadOnlySpan<byte> line, int count)
    {
        Span<float> results = stackalloc float[count];
        int index = 0;

        int start = 0;
        while (index < count && start < line.Length)
        {
            while (start < line.Length && line[start] == ' ')
                start++;

            if (start >= line.Length)
                break;

            int end = start;
            while (end < line.Length && line[end] != ' ')
                end++;

            if (end > start && index < count)
            {
                results[index++] = ParseFloat(line[start..end]);
            }

            start = end + 1;
        }

        float[] result = new float[count];
        results.CopyTo(result);
        return result;
    }

    private static float ParseFloat(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return 0f;

        bool negative = false;
        int index = 0;

        if (span[0] == '-')
        {
            negative = true;
            index++;
        }
        else if (span[0] == '+')
        {
            index++;
        }

        float result = 0f;
        while (index < span.Length && span[index] >= '0' && span[index] <= '9')
        {
            result = result * 10 + (span[index] - '0');
            index++;
        }

        if (index < span.Length && span[index] == '.')
        {
            index++;
            float fraction = 0.1f;
            while (index < span.Length && span[index] >= '0' && span[index] <= '9')
            {
                result += (span[index] - '0') * fraction;
                fraction *= 0.1f;
                index++;
            }
        }

        if (index < span.Length && (span[index] == 'e' || span[index] == 'E'))
        {
            index++;
            bool expNegative = false;
            if (index < span.Length && span[index] == '-')
            {
                expNegative = true;
                index++;
            }
            else if (index < span.Length && span[index] == '+')
            {
                index++;
            }

            int exponent = 0;
            while (index < span.Length && span[index] >= '0' && span[index] <= '9')
            {
                exponent = exponent * 10 + (span[index] - '0');
                index++;
            }

            if (expNegative)
                exponent = -exponent;

            result *= MathF.Pow(10, exponent);
        }

        return negative ? -result : result;
    }

    private void ParseFace(ReadOnlySpan<byte> line)
    {
        int faceVertexCount = 0;
        int[]? rentedVertices = null;
        int[]? rentedUVs = null;
        int[]? rentedNormals = null;

        try
        {
            int start = 0;
            while (start < line.Length)
            {
                while (start < line.Length && line[start] == ' ')
                    start++;

                if (start >= line.Length)
                    break;

                int end = start;
                while (end < line.Length && line[end] != ' ')
                    end++;

                if (end > start)
                {
                    EnsureCapacity(ref rentedVertices, ref _tempFaceVertices, faceVertexCount + 1);
                    EnsureCapacity(ref rentedUVs, ref _tempFaceUVs, faceVertexCount + 1);
                    EnsureCapacity(ref rentedNormals, ref _tempFaceNormals, faceVertexCount + 1);

                    ParseFaceVertex(
                        line[start..end],
                        out _tempFaceVertices![faceVertexCount],
                        out _tempFaceUVs![faceVertexCount],
                        out _tempFaceNormals![faceVertexCount]);

                    faceVertexCount++;
                }

                start = end + 1;
            }

            if (faceVertexCount < 3)
                return;

            for (int i = 1; i < faceVertexCount - 1; i++)
            {
                AddVertex(_tempFaceVertices![0], _tempFaceUVs![0], _tempFaceNormals![0]);
                AddVertex(_tempFaceVertices![i], _tempFaceUVs![i], _tempFaceNormals![i]);
                AddVertex(_tempFaceVertices![i + 1], _tempFaceUVs![i + 1], _tempFaceNormals![i + 1]);
            }
        }
        finally
        {
            if (rentedVertices != null)
                ArrayPool<int>.Shared.Return(rentedVertices);
            if (rentedUVs != null)
                ArrayPool<int>.Shared.Return(rentedUVs);
            if (rentedNormals != null)
                ArrayPool<int>.Shared.Return(rentedNormals);
        }
    }

    private void EnsureCapacity(ref int[]? rented, ref int[]? tempField, int needed)
    {
        if (tempField != null && tempField.Length >= needed)
            return;

        if (rented != null)
            ArrayPool<int>.Shared.Return(rented);

        rented = ArrayPool<int>.Shared.Rent(needed);
        tempField = rented;
    }

    private void ParseFaceVertex(ReadOnlySpan<byte> span, out int vertexIndex, out int uvIndex, out int normalIndex)
    {
        vertexIndex = -1;
        uvIndex = -1;
        normalIndex = -1;

        int slash1 = span.IndexOf((byte)'/');
        if (slash1 < 0)
        {
            vertexIndex = ParseInt(span);
            return;
        }

        vertexIndex = ParseInt(span[..slash1]);

        int slash2 = span[(slash1 + 1)..].IndexOf((byte)'/');
        if (slash2 < 0)
        {
            uvIndex = ParseInt(span[(slash1 + 1)..]);
        }
        else
        {
            if (slash2 > 0)
                uvIndex = ParseInt(span[(slash1 + 1)..(slash1 + 1 + slash2)]);

            normalIndex = ParseInt(span[(slash1 + 1 + slash2 + 1)..]);
        }
    }

    private static int ParseInt(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return 0;

        bool negative = false;
        int index = 0;

        if (span[0] == '-')
        {
            negative = true;
            index++;
        }
        else if (span[0] == '+')
        {
            index++;
        }

        int result = 0;
        while (index < span.Length && span[index] >= '0' && span[index] <= '9')
        {
            result = result * 10 + (span[index] - '0');
            index++;
        }

        return negative ? -result : result;
    }

    private void AddVertex(int positionIdx, int uvIdx, int normalIdx)
    {
        int posIndex = NormalizeIndex(positionIdx, _positions.Count);
        int uvIndex = NormalizeIndex(uvIdx, _uvs.Count);
        int normIndex = NormalizeIndex(normalIdx, _normals.Count);

        var key = new VertexKey(posIndex, uvIndex, normIndex);

        if (_vertexMap.TryGetValue(key, out int existingIndex))
        {
            _indices.Add((uint)existingIndex);
            return;
        }

        Vector3 position = posIndex >= 0 && posIndex < _positions.Count
            ? _positions[posIndex]
            : Vector3.Zero;

        Vector2 uv = uvIndex >= 0 && uvIndex < _uvs.Count
            ? _uvs[uvIndex]
            : Vector2.Zero;

        Vector3 normal = normIndex >= 0 && normIndex < _normals.Count
            ? _normals[normIndex]
            : Vector3.UnitZ;

        int newIndex = _vertices.Count;
        _vertices.Add(new VertexPositionNormalTexture(position, normal, uv));
        _vertexMap[key] = newIndex;
        _indices.Add((uint)newIndex);
    }

    private static int NormalizeIndex(int index, int count)
    {
        if (index < 0)
            return count + index;
        if (index > 0)
            return index - 1;
        return -1;
    }

    private readonly struct VertexKey(int position, int uv, int normal) : IEquatable<VertexKey>
    {
        private readonly int _position = position;
        private readonly int _uv = uv;
        private readonly int _normal = normal;

        public bool Equals(VertexKey other) =>
            _position == other._position && _uv == other._uv && _normal == other._normal;

        public override int GetHashCode() => HashCode.Combine(_position, _uv, _normal);

        public override bool Equals(object? obj) => obj is VertexKey other && Equals(other);
    }
}

/// <summary>
/// Represents the result of parsing an OBJ file.
/// </summary>
/// <param name="Vertices">The array of vertices parsed from the OBJ file.</param>
/// <param name="Indices">The array of indices parsed from the OBJ file.</param>
public readonly struct ObjParseResult(VertexPositionNormalTexture[] vertices, uint[] indices)
{
    /// <summary>
    /// The array of vertices parsed from the OBJ file.
    /// </summary>
    public readonly VertexPositionNormalTexture[] Vertices = vertices;

    /// <summary>
    /// The array of indices parsed from the OBJ file.
    /// </summary>
    public readonly uint[] Indices = indices;
}

file ref struct ObjLineReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position = 0;

    public bool ReadLine(out ReadOnlySpan<byte> line)
    {
        if (_position >= _data.Length)
        {
            line = [];
            return false;
        }

        int lineEnd = _data[_position..].IndexOfAny((byte)'\r', (byte)'\n');

        if (lineEnd < 0)
        {
            line = Trim(_data[_position..]);
            _position = _data.Length;
            return !line.IsEmpty;
        }

        line = Trim(_data[_position..(_position + lineEnd)]);

        int skip = 1;
        if (_data[_position + lineEnd] == '\r' &&
            _position + lineEnd + 1 < _data.Length &&
            _data[_position + lineEnd + 1] == '\n')
        {
            skip = 2;
        }

        _position += lineEnd + skip;
        return !line.IsEmpty;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
    {
        int start = 0;
        while (start < span.Length && (span[start] == ' ' || span[start] == '\t'))
            start++;

        int end = span.Length - 1;
        while (end >= start && (span[end] == ' ' || span[end] == '\t'))
            end--;

        return span[start..(end + 1)];
    }
}
