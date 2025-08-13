// modified from https://github.com/microsoft/OpenAPI.NET/blob/main/src/Microsoft.OpenApi/JsonPointer.cs
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Text.Json.Nodes;

//namespace Microsoft.OpenApi
namespace Alco.Engine;

/// <summary>
/// JSON pointer.
/// </summary>
public class JsonPointer
{
    /// <summary>
    /// Initializes the <see cref="JsonPointer"/> class.
    /// </summary>
    /// <param name="pointer">Pointer as string.</param>
    public JsonPointer(string pointer)
    {
        Tokens = string.IsNullOrEmpty(pointer) || pointer == "/"
            ? new string[0]
            : pointer.Split('/').Skip(1).Select(Decode).ToArray();
    }

    /// <summary>
    /// Initializes the <see cref="JsonPointer"/> class.
    /// </summary>
    /// <param name="tokens">Pointer as tokenized string.</param>
    private JsonPointer(string[] tokens)
    {
        Tokens = tokens;
    }

    /// <summary>
    /// Tokens.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    /// Gets the parent pointer.
    /// </summary>
    public JsonPointer? ParentPointer
    {
        get
        {
            if (Tokens.Length == 0)
            {
                return null;
            }

            return new(Tokens.Take(Tokens.Length - 1).ToArray());
        }
    }

    public JsonNode? Find(JsonNode? baseJsonNode)
    {
        if (baseJsonNode is null)
        {
            return null;
        }

        if (Tokens.Length == 0)
        {
            return baseJsonNode;
        }

        try
        {
            var pointer = baseJsonNode;
            foreach (var token in Tokens)
            {
                if (pointer is JsonArray array && int.TryParse(token, out var tokenValue))
                {
                    pointer = array[tokenValue];
                }
                else if (pointer is JsonObject map && !map.TryGetPropertyValue(token, out pointer))
                {
                    return null;
                }
            }

            return pointer;
        }
        catch (Exception)
        {
            return null;
        }
    }


    /// <summary>
    /// Decode the string.
    /// </summary>
    private string Decode(string token)
    {
        return Uri.UnescapeDataString(token).Replace("~1", "/").Replace("~0", "~");
    }

    /// <summary>
    /// Gets the string representation of this JSON pointer.
    /// </summary>
    public override string ToString()
    {
        return "/" + string.Join("/", Tokens);
    }
}
