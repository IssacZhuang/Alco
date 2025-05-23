using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Alco.Engine;

public static class UtilsJson
{
    public static string Merge(JsonDocument parent, JsonDocument target)
    {
        var outputBuffer = new ArrayBufferWriter<byte>();

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
                MergeObjects(jsonWriter, rootParent, rootTarget);
            }
        }

        return Encoding.UTF8.GetString(outputBuffer.WrittenSpan);
    }

    public static JsonDocument MergeToDocument(JsonDocument target, JsonDocument parent)
    {
        return JsonDocument.Parse(Merge(target, parent));
    }

    private static void MergeObjects(Utf8JsonWriter jsonWriter, JsonElement parent, JsonElement target)
    {
        // Debug.Assert(root1.ValueKind == JsonValueKind.Object);
        // Debug.Assert(root2.ValueKind == JsonValueKind.Object);

        jsonWriter.WriteStartObject();

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
                    MergeObjects(jsonWriter, originalValue, newValue); // Recursive call
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
                property.WriteTo(jsonWriter);
            }
        }

        jsonWriter.WriteEndObject();
    }

    private static void MergeArrays(Utf8JsonWriter jsonWriter, JsonElement root1, JsonElement root2)
    {
        // Debug.Assert(root1.ValueKind == JsonValueKind.Array);
        // Debug.Assert(root2.ValueKind == JsonValueKind.Array);

        jsonWriter.WriteStartArray();

        // Write all the elements from both JSON arrays
        foreach (JsonElement element in root1.EnumerateArray())
        {
            element.WriteTo(jsonWriter);
        }
        foreach (JsonElement element in root2.EnumerateArray())
        {
            element.WriteTo(jsonWriter);
        }

        jsonWriter.WriteEndArray();
    }
}
