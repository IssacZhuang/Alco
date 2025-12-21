using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Loads JSON files from registered <see cref="IFileSource"/> instances, builds an in-memory index
/// of JSON documents, and processes inheritance/merge rules across items. Provides events that fire
/// immediately before and after the processing phase.
/// </summary>
public class JsonPreprocessor
{
    private static readonly HashSet<string> _specialKeywords = new() { "$abstract", "$parent" };

    public const string Keyward_Abstract = "$abstract";
    public const string Keyward_Parent = "$parent";
    public const string Keyward_Id = "Id";

    /// <summary>
    /// Context passed to <see cref="BeforeProcessJsonDocument"/> providing access to the
    /// currently loaded JSON items. This is a live view into the preprocessor state
    /// immediately after loading and before processing.
    /// </summary>
    private sealed class Context : IJsonPreprocessContext
    {
        private readonly JsonPreprocessor _preprocessor;
        private readonly Dictionary<string, JsonNode> _pendingNodeEdits;
        private readonly Dictionary<string, JsonNode> _pendingAbstractNodeEdits;

        /// <summary>
        /// Initializes a new <see cref="Context"/> for the specified preprocessor.
        /// </summary>
        /// <param name="preprocessor">The owning <see cref="JsonPreprocessor"/>.</param>
        internal Context(JsonPreprocessor preprocessor)
        {
            _preprocessor = preprocessor;
            _pendingNodeEdits = new Dictionary<string, JsonNode>();
            _pendingAbstractNodeEdits = new Dictionary<string, JsonNode>();
        }

        /// <summary>
        /// Tries to get a document as a mutable <see cref="JsonNode"/> by its Id.
        /// The returned node is cached and will be applied automatically after the event completes.
        /// </summary>
        /// <param name="id">The document Id.</param>
        /// <param name="node">The resulting <see cref="JsonNode"/> when found.</param>
        /// <returns>True if found; otherwise false.</returns>
        public bool TryGetDocumentNode(string id, [NotNullWhen(true)] out JsonNode? node)
        {
            if (_pendingNodeEdits.TryGetValue(id, out var cached))
            {
                node = cached;
                return true;
            }
            if (_preprocessor._jsonItems.TryGetValue(id, out var jsonItem))
            {
                try{
                    node = JsonNode.Parse(jsonItem.Document.RootElement.GetRawText())!;
                    _pendingNodeEdits[id] = node;
                    return true;
                }
                catch (Exception ex)
                {
                    _preprocessor.AddError($"Failed to parse JSON file {jsonItem.Path}: {ex}");
                    node = null;
                    return false;
                }
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Tries to get an abstract document as a mutable <see cref="JsonNode"/> by its Id.
        /// The returned node is cached and will be applied automatically after the event completes.
        /// </summary>
        /// <param name="id">The abstract document Id.</param>
        /// <param name="node">The resulting <see cref="JsonNode"/> when found.</param>
        /// <returns>True if found; otherwise false.</returns>
        public bool TryGetAbstractDocumentNode(string id, [NotNullWhen(true)] out JsonNode? node)
        {
            if (_pendingAbstractNodeEdits.TryGetValue(id, out var cached))
            {
                node = cached;
                return true;
            }
            if (_preprocessor._abstractJsonItems.TryGetValue(id, out var jsonItem))
            {
                try{
                node = JsonNode.Parse(jsonItem.Document.RootElement.GetRawText())!;
                    _pendingAbstractNodeEdits[id] = node;
                    return true;
                }
                catch (Exception ex)
                {
                    _preprocessor.AddError($"Failed to parse JSON file {jsonItem.Path}: {ex}");
                    node = null;
                    return false;
                }
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Applies all cached node edits to the underlying documents.
        /// </summary>
        internal void ApplyPendingEdits()
        {
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            foreach (var pair in _pendingNodeEdits)
            {
                if (_preprocessor._jsonItems.TryGetValue(pair.Key, out var jsonItem))
                {
                    var newDoc = JsonDocument.Parse(pair.Value.ToJsonString(), options);
                    _preprocessor._jsonItems[pair.Key] = new JsonItem(jsonItem.Path, newDoc);

                    int index = _preprocessor._jsonItemsList.IndexOf(jsonItem.Document);
                    if (index >= 0)
                    {
                        _preprocessor._jsonItemsList[index] = newDoc;
                    }
                    else
                    {
                        _preprocessor._jsonItemsList.Add(newDoc);
                    }
                }
            }

            foreach (var pair in _pendingAbstractNodeEdits)
            {
                if (_preprocessor._abstractJsonItems.TryGetValue(pair.Key, out var jsonItem))
                {
                    var newDoc = JsonDocument.Parse(pair.Value.ToJsonString(), options);
                    _preprocessor._abstractJsonItems[pair.Key] = new JsonItem(jsonItem.Path, newDoc);
                }
            }
        }
    }

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

    private readonly Dictionary<string, JsonItem> _abstractJsonItems = new();
    private readonly Dictionary<string, JsonItem> _jsonItems = new();
    private readonly List<JsonDocument> _jsonItemsList = new();
    private readonly List<IFileSource> _fileSources = new();
    private readonly ArrayBuffer<JsonDocument?> _tempJsonDocuments = new();

    private readonly Action<string> _onError;

    /// <summary>
    /// Occurs right before processing JSON items. Raised after all JSON files are loaded but before
    /// inheritance/merge is performed. Provides a live <see cref="Context"/> view to inspect and
    /// query the loaded items.
    /// </summary>
    public event Action<IJsonPreprocessContext>? BeforeProcessJsonDocument;

    public IEnumerable<JsonDocument> AllDocuments
    {
        get
        {
            foreach (var item in _jsonItems)
            {
                yield return item.Value.Document;
            }
        }
    }

    public JsonPreprocessor(Action<string> onError)
    {
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

    /// <summary>
    /// Loads all JSON files from registered sources then processes items (e.g., inheritance/merge).
    /// Raises <see cref="BeforeProcessJsonDocument"/> before processing.
    /// </summary>
    public void Preprocess()
    {
        LoadJsonItems();
        var ctx = new Context(this);
        BeforeProcessJsonDocument?.Invoke(ctx);
        ctx.ApplyPendingEdits();
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
                if (filePath.EndsWith(FileExt.TextJSON) || filePath.EndsWith(FileExt.TextJSONC))
                {
                    jsonFiles.Add((filePath, fileSource));
                }
            }
        }

        //JsonDocument?[] jsonDocuments = new JsonDocument?[jsonFiles.Count];
        _tempJsonDocuments.SetSizeWithoutCopy(jsonFiles.Count);
        _tempJsonDocuments.Clear();

        _abstractJsonItems.Clear();
        _jsonItems.Clear();
        _jsonItemsList.Clear();

        //parallel load json documents
        Parallel.For(0, jsonFiles.Count, i =>
        {
            var (filePath, fileSource) = jsonFiles[i];

            if (fileSource.TryGetData(filePath, out var data, out var failureReason))
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(data.AsReadOnlySpan());

                    // Use JsonDocumentOptions to support JSONC (comments and trailing commas)
                    var options = new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    JsonDocument document = JsonDocument.Parse(json, options);

                    _tempJsonDocuments[i] = document;
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


        for (int i = 0; i < jsonFiles.Count; i++)
        {
            var (filePath, fileSource) = jsonFiles[i];
            JsonDocument? document = _tempJsonDocuments[i];
            if (document == null)
            {
                continue;
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                AddError($"Json file {filePath} is not a valid object");
                continue;
            }
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
            AddError($"JSON item {currentId} has empty parent ID");
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
            var mergedJson = JsonUtility.Merge(processedParent, document, _specialKeywords);

            // Use JsonDocumentOptions to support JSONC (comments and trailing commas)
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var mergedDocument = JsonDocument.Parse(mergedJson, options);

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
            else if (_jsonItems.TryAdd(id, new JsonItem(filePath, document)))
            {
                _jsonItemsList.Add(document);
            }
            else
            {
                AddError($"Failed to add json item {id} from {filePath}");
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

    private void AddError(string message)
    {
        _onError?.Invoke(message);
    }
}
