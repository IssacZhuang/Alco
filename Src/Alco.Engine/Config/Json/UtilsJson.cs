using System.Buffers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Alco.Engine;

/// <summary>
/// Provides utilities for merging JSON documents with support for inheritance control.
/// Supports both object and array merging with special directives for controlling merge behavior.
/// </summary>
public static class UtilsJson
{
    /// <summary>
    /// The special property name used to control inheritance behavior in JSON objects and arrays.
    /// When set to false, completely overrides the parent instead of merging.
    /// </summary>
    public const string Keyword_Inherit = "$inherit";


    /// <summary>
    /// Array inheritance mode that appends target elements after parent elements.
    /// This is the default behavior when no inheritance control is specified.
    /// </summary>
    public const string InheritMode_Append = "append";


    /// <summary>
    /// Array inheritance mode that prepends target elements before parent elements.
    /// </summary>
    public const string InheritMode_Prepend = "prepend";

    /// <summary>
    /// Merges two JSON documents with support for inheritance control directives.
    /// </summary>
    /// <param name="parent">The parent JSON document to merge content into.</param>
    /// <param name="target">The target JSON document whose content will be merged into the parent.</param>
    /// <returns>A JSON string representing the merged result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either document is not a container type (object or array) or when container types don't match.
    /// </exception>
    /// <remarks>
    /// <para>For objects:</para>
    /// <list type="bullet">
    /// <item>Properties are recursively merged by default</item>
    /// <item>If target contains "$inherit": false, completely overrides parent object</item>
    /// <item>The "$inherit" property is always excluded from the final result</item>
    /// </list>
    /// <para>For arrays:</para>
    /// <list type="bullet">
    /// <item>Target array replaces parent array by default (no merging)</item>
    /// <item>First element can be a control object with "$inherit" property to enable merging:
    ///   <list type="bullet">
    ///   <item>false: Replace parent array entirely (same as default)</item>
    ///   <item>true: Append target elements after parent elements</item>
    ///   <item>"prepend": Target elements first, then parent elements</item>
    ///   <item>"append": Parent elements first, then target elements</item>
    ///   </list>
    /// </item>
    /// <item>Control objects are excluded from the final result</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Object merging with inheritance control
    /// var parent = JsonDocument.Parse(@"{""name"": ""parent"", ""age"": 30}");
    /// var target = JsonDocument.Parse(@"{""$inherit"": false, ""name"": ""child""}");
    /// var result = UtilsJson.Merge(parent, target);
    /// // Result: {"name": "child"}
    /// 
    /// // Array merging with explicit inheritance control
    /// var parent = JsonDocument.Parse(@"[""a"", ""b""]");
    /// var target = JsonDocument.Parse(@"[{""$inherit"": ""append""}, ""x"", ""y""]");
    /// var result = UtilsJson.Merge(parent, target);
    /// // Result: ["a", "b", "x", "y"]
    /// 
    /// // Array replacement (default behavior)
    /// var parent = JsonDocument.Parse(@"[""a"", ""b""]");
    /// var target = JsonDocument.Parse(@"[""x"", ""y""]");
    /// var result = UtilsJson.Merge(parent, target);
    /// // Result: ["x", "y"]
    /// </code>
    /// </example>
    public static string Merge(JsonDocument parent, JsonDocument target, ReadOnlySpan<string> ignoreProperties = default)
    {
        var outputBuffer = new ArrayBufferWriter<byte>();
        HashSet<string> ignorePropertiesSet = new();
        for (int i = 0; i < ignoreProperties.Length; i++)
        {
            ignorePropertiesSet.Add(ignoreProperties[i]);
        }

        using (var jsonWriter = new Utf8JsonWriter(outputBuffer, new JsonWriterOptions { Indented = true }))
        {
            JsonElement rootParent = parent.RootElement;
            JsonElement rootTarget = target.RootElement;

            if (rootParent.ValueKind != JsonValueKind.Array && rootParent.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"The original JSON document to merge new content into must be a container type. Instead it is {rootParent.ValueKind}.");
            }

            if (rootParent.ValueKind != rootTarget.ValueKind)
            {
                throw new InvalidOperationException($"The original JSON document to merge new content into must be a container type. Instead it is {rootTarget.ValueKind}.");
            }

            if (rootParent.ValueKind == JsonValueKind.Array)
            {
                MergeArrays(jsonWriter, rootParent, rootTarget);
            }
            else
            {
                MergeObjects(jsonWriter, rootParent, rootTarget, ignorePropertiesSet);
            }
        }

        return Encoding.UTF8.GetString(outputBuffer.WrittenSpan);
    }

    /// <summary>
    /// Same as <see cref="Merge(JsonDocument, JsonDocument)"/>, but returns a <see cref="JsonDocument"/> instead of a string.
    /// </summary>
    /// <param name="target">The target JSON document whose content will be merged into the parent.</param>
    /// <param name="parent">The parent JSON document to merge content into.</param>
    /// <returns></returns>
    public static JsonDocument MergeToDocument(JsonDocument target, JsonDocument parent)
    {
        return JsonDocument.Parse(Merge(target, parent));
    }

    private static void MergeObjects(Utf8JsonWriter jsonWriter, JsonElement parent, JsonElement target, HashSet<string> ignoreProperties)
    {
        // Debug.Assert(root1.ValueKind == JsonValueKind.Object);
        // Debug.Assert(root2.ValueKind == JsonValueKind.Object);

        jsonWriter.WriteStartObject();

        // Check if target has $inherit property set to false
        if (target.TryGetProperty(Keyword_Inherit, out JsonElement inheritValue) &&

            inheritValue.ValueKind == JsonValueKind.False)
        {
            // If $inherit is false, write all properties from target except $inherit itself
            foreach (JsonProperty property in target.EnumerateObject())
            {
                if (property.Name == Keyword_Inherit || ignoreProperties.Contains(property.Name))
                {
                    continue;
                }

                property.WriteTo(jsonWriter);
            }
            jsonWriter.WriteEndObject();
            return;
        }

        // Original merging logic when $inherit is not false
        // Write all the properties of the first document.
        // If a property exists in both documents, either:
        // * Merge them, if the value kinds match (e.g. both are objects or arrays),
        // * Completely override the value of the first with the one from the second, if the value kind mismatches (e.g. one is object, while the other is an array or string),
        // * Or favor the value of the first (regardless of what it may be), if the second one is null (i.e. don't override the first).
        foreach (JsonProperty property in parent.EnumerateObject())
        {
            string propertyName = property.Name;

            JsonValueKind newValueKind;

            if (target.TryGetProperty(propertyName, out JsonElement newValue) && (newValueKind = newValue.ValueKind) != JsonValueKind.Null)
            {
                jsonWriter.WritePropertyName(propertyName);

                JsonElement originalValue = property.Value;
                JsonValueKind originalValueKind = originalValue.ValueKind;

                if (newValueKind == JsonValueKind.Object && originalValueKind == JsonValueKind.Object)
                {
                    MergeObjects(jsonWriter, originalValue, newValue, ignoreProperties); // Recursive call
                }
                else if (newValueKind == JsonValueKind.Array && originalValueKind == JsonValueKind.Array)
                {
                    MergeArrays(jsonWriter, originalValue, newValue);
                }
                else
                {
                    newValue.WriteTo(jsonWriter);
                }
            }
            else
            {
                property.WriteTo(jsonWriter);
            }
        }

        // Write all the properties of the second document that are unique to it.
        foreach (JsonProperty property in target.EnumerateObject())
        {
            if (!parent.TryGetProperty(property.Name, out _))
            {
                // Skip the $inherit property when writing unique properties
                if (property.Name != Keyword_Inherit)
                {
                    property.WriteTo(jsonWriter);
                }
            }
        }

        jsonWriter.WriteEndObject();
    }

    private static void MergeArrays(Utf8JsonWriter jsonWriter, JsonElement parent, JsonElement target)
    {
        // Debug.Assert(parent.ValueKind == JsonValueKind.Array);
        // Debug.Assert(target.ValueKind == JsonValueKind.Array);

        jsonWriter.WriteStartArray();

        // Check if target array has control object as first element
        var targetArray = target.EnumerateArray().ToArray();
        if (targetArray.Length > 0 && targetArray[0].ValueKind == JsonValueKind.Object)
        {
            var firstElement = targetArray[0];
            if (firstElement.TryGetProperty(Keyword_Inherit, out JsonElement inheritValue))
            {
                // Handle different inheritance modes
                if (inheritValue.ValueKind == JsonValueKind.False)
                {
                    // Replace mode: only write target elements (excluding control object)
                    for (int i = 1; i < targetArray.Length; i++)
                    {
                        targetArray[i].WriteTo(jsonWriter);
                    }
                    jsonWriter.WriteEndArray();
                    return;
                }
                else if (inheritValue.ValueKind == JsonValueKind.String)
                {
                    string? mode = inheritValue.GetString();
                    if (mode == InheritMode_Prepend)
                    {
                        // Prepend mode: target elements first, then parent elements
                        for (int i = 1; i < targetArray.Length; i++)
                        {
                            targetArray[i].WriteTo(jsonWriter);
                        }
                        foreach (JsonElement element in parent.EnumerateArray())
                        {
                            element.WriteTo(jsonWriter);
                        }
                        jsonWriter.WriteEndArray();
                        return;
                    }
                    else if (mode == InheritMode_Append)
                    {
                        // Append mode: parent elements first, then target elements
                        foreach (JsonElement element in parent.EnumerateArray())
                        {
                            element.WriteTo(jsonWriter);
                        }
                        for (int i = 1; i < targetArray.Length; i++)
                        {
                            targetArray[i].WriteTo(jsonWriter);
                        }
                        jsonWriter.WriteEndArray();
                        return;
                    }
                }
                else if (inheritValue.ValueKind == JsonValueKind.True)
                {
                    // True mode: append target to parent (backward compatibility)
                    foreach (JsonElement element in parent.EnumerateArray())
                    {
                        element.WriteTo(jsonWriter);
                    }
                    for (int i = 1; i < targetArray.Length; i++)
                    {
                        targetArray[i].WriteTo(jsonWriter);
                    }
                    jsonWriter.WriteEndArray();
                    return;
                }
            }
        }

        // Default behavior: replace parent with target (no merging)
        foreach (JsonElement element in target.EnumerateArray())
        {
            element.WriteTo(jsonWriter);
        }

        jsonWriter.WriteEndArray();
    }
}
