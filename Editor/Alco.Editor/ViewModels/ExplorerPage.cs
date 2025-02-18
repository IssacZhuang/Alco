using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alco.Editor.ViewModels;

public class ExplorerPage : Page
{
    public override string IconData => "M903.253 231.253V682.24L855.467 736H679.253v170.24L625.493 960H174.507l-53.76-53.76V344.747L174.507 288h170.24V120.747L398.507 64H736l167.253 167.253z m-167.253 0h89.6l-89.6-89.6v89.6zM622.507 736h-224l-53.76-53.76V344.747h-170.24v558.507h448V736z m224-448H679.253V120.747H398.507v558.507h448V288z";
    public override string Tooltip => "Explorer";
    public override string Name => "Explorer";

    public override Control Control { get; }

    public ExplorerPage()
    {
        Control = new Views.ExplorerPage()
        {
            DataContext = this
        };
    }
}

