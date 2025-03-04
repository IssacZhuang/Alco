using System.Runtime.InteropServices;
using Alco.IO;


namespace Alco.Project;

public class AlcoProject : BaseConfig
{
    public DotnetSolution? DotnetSolution { get; set; }
    public string EntryProject { get; set; } = string.Empty;
}
