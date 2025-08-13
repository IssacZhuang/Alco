using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Alco.Engine;

/// <summary>
/// Extensions for JSON pointers.
/// </summary>
public static class JsonNodeExtensions
{
    /// <summary>
    /// Finds the JSON node that corresponds to this JSON pointer based on the base Json node.
    /// </summary>
    public static JsonNode? Find(this JsonNode? baseJsonNode, JsonPointer pointer)
    {
        return pointer.Find(baseJsonNode);
    }

    /// <summary>
    /// Finds the JSON node that corresponds to this JSON pointer based on the base Json node.
    /// </summary>
    public static JsonNode? Find(this JsonNode? baseJsonNode, string pointer)
    {
        return new JsonPointer(pointer).Find(baseJsonNode);
    }
}
