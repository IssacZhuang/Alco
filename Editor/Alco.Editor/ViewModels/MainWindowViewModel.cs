using System.Collections;
using System.Collections.Generic;
using Alco.Editor.Models;

namespace Alco.Editor.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public string Greeting { get; } = "Welcome to Avalonia!";
        public IReadOnlyList<Page> Pages { get; } = [new ExplorerPage()];
    }
}
