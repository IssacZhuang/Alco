using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Alco.IO;

namespace Alco.Engine;

public partial class JsonPreprocessor
{
    public const string Keyward_Abstract = "$abstract";
    public const string Keyward_Parent = "$parent";
    public const string Keyward_Id = "Id";

    private class JsonItem
    {
        public string Path { get; }
        public JsonDocument Document { get; }

        public JsonItem(string path, JsonDocument document)
        {
            Path = path;
            Document = document;
        }
    }

    private readonly ConcurrentDictionary<string, JsonItem> _abstractJsonItems = new();
    private readonly ConcurrentDictionary<string, JsonItem> _jsonItems = new();
    private readonly List<IFileSource> _fileSources = new();

    private readonly Action<string> _onInfo;
    private readonly Action<string> _onWarning;
    private readonly Action<string> _onError;

    public IEnumerable<JsonDocument> AllDocuments => _jsonItems.Values.Select(item => item.Document);

    public JsonPreprocessor(Action<string> onInfo, Action<string> onWarning, Action<string> onError)
    {
        _onInfo = onInfo;
        _onWarning = onWarning;
        _onError = onError;
    }

    public void AddFileSource(IFileSource fileSource)
    {
        _fileSources.Add(fileSource);
    }

    public void RemoveFileSource(IFileSource fileSource)
    {
        _fileSources.Remove(fileSource);
    }

    public JsonDocument GetJsonDocument(string id)
    {
        if (_jsonItems.TryGetValue(id, out var jsonItem))
        {
            return jsonItem.Document;
        }

        throw new Exception($"Json document with id {id} not found");
    }

    public bool TryGetJsonDocument(string id, [NotNullWhen(true)] out JsonDocument? document)
    {
        if (_jsonItems.TryGetValue(id, out var jsonItem))
        {
            document = jsonItem.Document;
            return true;
        }
        document = null;
        return false;
    }

    public void Preprocess()
    {
        LoadJsonItems();
        ProcessJsonItems();
    }

    private void LoadJsonItems()
    {
        List<(string filePath, IFileSource fileSource)> jsonFiles = new();
        //all .json file in all file sources
        foreach (var fileSource in _fileSources)
        {
            foreach (var filePath in fileSource.AllFileNames)
            {
                if (filePath.EndsWith(FileExt.TextJSON))
                {
                    jsonFiles.Add((filePath, fileSource));
                }
            }
        }

        _abstractJsonItems.Clear();
        _jsonItems.Clear();

        Parallel.ForEach(jsonFiles, fileInfo =>
        {
            var (filePath, fileSource) = fileInfo;

            if (fileSource.TryGetData(filePath, out var data, out var failureReason))
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(data.Span);
                    JsonDocument document = JsonDocument.Parse(json);

                    //check if the json file is abstract
                    if (document.RootElement.TryGetProperty(Keyward_Abstract, out var abstractProperty) && abstractProperty.GetBoolean())
                    {
                        AddAbstractJsonItem(document, filePath);
                    }
                    else
                    {
                        AddJsonItem(document, filePath);
                    }
                }
                catch (Exception ex)
                {
                    AddError($"Failed to parse JSON file {filePath}: {ex.Message}");
                }
                finally
                {
                    data.Dispose();
                }
            }
            else
            {
                AddError($"Failed to read file {filePath}: {failureReason}");
            }
        });
    }

    private void ProcessJsonItems()
    {
        // Process each non-abstract JSON item for inheritance
        var itemsToProcess = _jsonItems.ToArray(); // Create a copy to avoid modification during iteration

        foreach (var kvp in itemsToProcess)
        {
            var id = kvp.Key;
            var jsonItem = kvp.Value;

            try
            {
                var processedDocument = ProcessJsonItem(jsonItem.Document, new HashSet<string>(), id);

                // Replace the original document with the processed one if it changed
                if (processedDocument != jsonItem.Document)
                {
                    _jsonItems[id] = new JsonItem(jsonItem.Path, processedDocument);
                    AddInfo($"Processed inheritance for JSON item {id} from {jsonItem.Path}");
                }
            }
            catch (Exception ex)
            {
                AddError($"Error processing JSON item {id} from {jsonItem.Path}: {ex.Message}");
            }
        }
    }

    private JsonDocument ProcessJsonItem(JsonDocument document, HashSet<string> visitedIds, string currentId)
    {
        var rootElement = document.RootElement;

        // Check if this item has a parent
        if (!rootElement.TryGetProperty(Keyward_Parent, out var parentProperty))
        {
            return document; // No parent, return as-is
        }

        var parentId = parentProperty.GetString();
        if (string.IsNullOrEmpty(parentId))
        {
            AddWarning($"JSON item {currentId} has empty parent ID");
            return document;
        }

        // Check for circular dependency
        if (visitedIds.Contains(parentId))
        {
            AddError($"Circular dependency detected in inheritance chain: {string.Join(" -> ", visitedIds)} -> {parentId}");
            return document;
        }

        // Find the parent (could be abstract or non-abstract)
        JsonDocument? parentDocument = null;
        string parentPath = "";

        if (_abstractJsonItems.TryGetValue(parentId, out var abstractParent))
        {
            parentDocument = abstractParent.Document;
            parentPath = abstractParent.Path;
        }
        else if (_jsonItems.TryGetValue(parentId, out var normalParent))
        {
            parentDocument = normalParent.Document;
            parentPath = normalParent.Path;
        }

        if (parentDocument == null)
        {
            AddError($"Parent with ID '{parentId}' not found for JSON item {currentId}");
            return document;
        }

        // Recursively process the parent first
        var newVisitedIds = new HashSet<string>(visitedIds) { currentId };
        var processedParent = ProcessJsonItem(parentDocument, newVisitedIds, parentId);

        try
        {
            // Merge parent with current document (current document overrides parent)
            var mergedJson = UtilsJson.Merge(processedParent, document);
            var mergedDocument = JsonDocument.Parse(mergedJson);

            AddInfo($"Merged JSON item {currentId} with parent {parentId}");
            return mergedDocument;
        }
        catch (Exception ex)
        {
            AddError($"Failed to merge JSON item {currentId} with parent {parentId}: {ex.Message}");
            return document;
        }
    }

    private void AddJsonItem(JsonDocument document, string filePath)
    {
        if (TryGetId(document.RootElement, out var id))
        {
            if (_jsonItems.TryGetValue(id, out var jsonItem))
            {
                AddError($"Json file {filePath} and {jsonItem.Path} have duplicate id {id}");
            }
            else
            {
                _jsonItems.TryAdd(id, new JsonItem(filePath, document));
            }
        }
        else
        {
            AddError($"Json file {filePath} has no id");
        }
    }

    private void AddAbstractJsonItem(JsonDocument document, string filePath)
    {
        if (TryGetId(document.RootElement, out var id))
        {
            if (_abstractJsonItems.TryGetValue(id, out var jsonItem))
            {
                AddError($"Abstract json file {filePath} and {jsonItem.Path} have duplicate id {id}");
            }
            else
            {
                _abstractJsonItems.TryAdd(id, new JsonItem(filePath, document));
            }
        }
        else
        {
            AddError($"Abstract json file {filePath} has no id");
        }
    }

    private static bool TryGetId(JsonElement element, [NotNullWhen(true)] out string? id)
    {
        if (element.TryGetProperty(Keyward_Id, out var idProperty))
        {
            id = idProperty.GetString();
            return id != null;
        }
        id = null;
        return false;
    }

    private void AddInfo(string message)
    {
        _onInfo?.Invoke(message);
    }

    private void AddWarning(string message)
    {
        _onWarning?.Invoke(message);
    }

    private void AddError(string message)
    {
        _onError?.Invoke(message);
    }
}
