using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Alco.IO;

namespace Alco.Editor.Models;

public class PreferenceConfig: BaseConfig
{
    public Dictionary<string, string> Strings { get; set; } = new();
    public Dictionary<string, float> Floats { get; set; } = new();
    public Dictionary<string, int> Ints { get; set; } = new();
    public Dictionary<string, bool> Bools { get; set; } = new();
    public string OpenedProject { get; set; } = string.Empty;

    
    
}

