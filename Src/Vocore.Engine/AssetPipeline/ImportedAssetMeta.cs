using System.Text;
using System.Text.Json;

namespace Vocore.Engine
{
    public struct ImportedAssetMeta
    {
        public string Filename { get; }
        public string ImportedName { get; }

        public ImportedAssetMeta(string filename, string importedName)
        {
            Filename = filename;
            ImportedName = importedName;
        }

        public string ToJson()
        {
            var options = new JsonWriterOptions
            {
                Indented = true
            };

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, options);

            writer.WriteStartObject();
            writer.WriteString(nameof(Filename), Filename);
            writer.WriteString(nameof(ImportedName), ImportedName);
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static ImportedAssetMeta FromJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string filename = root.GetProperty(nameof(Filename)).GetString() ?? throw new JsonException($"Filename is missing, Json text: {json}");
            string importedName = root.GetProperty(nameof(ImportedName)).GetString() ?? throw new JsonException($"ImportedName is missing, Json text: {json}");

            return new ImportedAssetMeta(filename, importedName);
        }
    }
}