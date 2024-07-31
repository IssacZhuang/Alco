using static SlangSharp.Slang;

namespace SlangSharp;

public class SlangCompiler : IDisposable
{
    private readonly SlangSession _session;
    private readonly BaseSlangFileSystem? _fileSystem;

    public SlangCompiler(BaseSlangFileSystem fileSystem, string sessionName = "unnamed_session")
    {
        _session = spCreateSession(sessionName);
        _fileSystem = fileSystem;
    }

    public SlangCompiler(string sessionName = "unnamed_session")
    {
        _session = spCreateSession(sessionName);
    }



    public void Dispose()
    {

    }
}