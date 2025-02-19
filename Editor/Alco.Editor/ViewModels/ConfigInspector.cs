using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alco.Editor.Attributes;
using Alco.Engine;
using Alco.IO;
using Avalonia.Controls;

namespace Alco.Editor.ViewModels;

[Inspector(typeof(BaseConfig), ".json")]
public partial class ConfigInspector : Inspector<BaseConfig>
{

    public override bool IsModified => false;

    public ConfigInspector()
    {
    }

    public override Control CreateControl()
    {
        return new Views.ConfigInspector()
        {
            DataContext = this
        };
    }

    protected override void OnOpenAsset(BaseConfig asset)
    {

    }
}
