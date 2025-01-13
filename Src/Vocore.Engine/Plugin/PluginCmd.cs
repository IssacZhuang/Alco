namespace Vocore.Engine;

public class PluginCmd : BaseEnginePlugin
{
    private struct Cmd
    {
        public string Name;
        public string[]? Args;
    }

    private class CmdSystem : BaseEngineSystem
    {
        private readonly CircularWorkStealingDeque<Cmd> _cmdQueue = new(8);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Thread _cmdThread;
        private readonly StreamReader _reader;

        public CmdSystem()
        {
            _reader = new StreamReader(Console.OpenStandardInput());
            _cmdThread = new Thread(() =>
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var readTask = _reader.ReadLineAsync().WaitAsync(_cancellationTokenSource.Token);
                    try
                    {
                        string? line = readTask.GetAwaiter().GetResult();
                        if (line == null)
                        {
                            continue;
                        }
                        string[] args = line.Split(' ');
                        _cmdQueue.Push(new Cmd { Name = args[0], Args = args.Length > 1 ? args[1..] : null });
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });
            _cmdThread.IsBackground = true;
            _cmdThread.Name = "CmdThread";
        }

        public override void OnStart()
        {
            _cmdThread.Start();
        }

        public override void OnTick(float delta)
        {
            StealingResult result = _cmdQueue.TrySteal(out Cmd cmd);
            if (result == StealingResult.Success)
            {
                ProcessCmd(cmd);
            }
        }

        public override void OnStop()
        {
            _reader.Close();
            _cancellationTokenSource.Cancel();
            _cmdThread.Join();
        }

        private void ProcessCmd(Cmd cmd)
        {
            //print
            Console.WriteLine($"Cmd: {cmd.Name}");
            if (cmd.Args != null)
            {
                Console.WriteLine($"Args: {string.Join(" ", cmd.Args)}");
            }
        }
    }

    public override void OnPostInitialize(GameEngine engine)
    {
        engine.AddSystem(new CmdSystem());
    }
}