

using System.Collections.Generic;
using System.Linq;

namespace Alco.Editor.ViewModels;

public class CreateInstanceDialog : ViewModelBase
{
    public List<TypeItem> Types { get; } = new List<TypeItem>();
    public TypeItem? SelectedType { get; set; }


    public CreateInstanceDialog()
    {
        Types.AddRange(App.Main.TypeDatabase.ConfigTypes.Select(t => new TypeItem(t)));
    }

    public Views.CreateInstanceDialog CreateControl()
    {
        return new Views.CreateInstanceDialog
        {
            DataContext = this
        };
    }


}
