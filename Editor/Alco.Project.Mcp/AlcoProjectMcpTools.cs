using System.ComponentModel;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace Alco.Project.Mcp;

[McpServerToolType]
public partial class AlcoProjectMcpTools
{
    private static IAlcoProjectContext? _context;

    public static IAlcoProjectContext Context => _context ?? throw new InvalidOperationException("Context is not set");

    public static void SetContext(IAlcoProjectContext context)
    {
        _context = context;
    }

    public static void ClearContext()
    {
        _context = null;
    }



    [McpServerTool, Description("Get the path of the opened project")]
    public static string GetOpenedProjectPath()
    {
        if (Context.Project == null)
        {
            return "No project is opened";
        }
        return Context.Project.ProjectDirectory;
    }
}
