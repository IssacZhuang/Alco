using System;
using System.Collections.Generic;

namespace Alco.Editor.Models;

public class FileEditorMeta
{
    private readonly HashSet<string> _supportedFormat = new();
    public Type Type { get; }

    public FileEditorMeta(Type type, params string[] supportedFormat)
    {
        Type = type;
        _supportedFormat.UnionWith(supportedFormat);
    }

    public bool IsSupported(string extension)
    {
        return _supportedFormat.Contains(extension.ToLower());
    }

    public FileEditor CreateInstance()
    {
        return (FileEditor)Activator.CreateInstance(Type)!;
    }
}