using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alco.Editor.Attributes;
using Alco.Engine;
using Alco.IO;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[Inspector(".json")]
public partial class ConfigInspector : Inspector<BaseConfig>
{
    public override Control Control { get; }

    public override bool IsModified => false;

    public ConfigInspector()
    {
        Control = new Views.ConfigInspector()
        {
            DataContext = this
        };
    }

    protected override void OnOpenAsset(BaseConfig asset)
    {

    }
}
