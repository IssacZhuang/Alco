using System.Text;
using System.Diagnostics;
using UnityEngine;

namespace UnityToolBox
{
    public static class BuiltinCommands
    {
        [RegisterCommand(Help = "Clear the command console", MaxArgCount = 0)]
        static void CommandClear(CommandArg[] args)
        {
            Terminal.Buffer.Clear();
        }

        [RegisterCommand(Help = "Display help information about a command", MaxArgCount = 1)]
        static void CommandHelp(CommandArg[] args)
        {
            if (args.Length == 0)
            {
                foreach (var command in Terminal.Shell.Commands)
                {
                    Terminal.Log("{0}: {1}", command.Key.PadRight(16), command.Value.help);
                }
                return;
            }

            string commandName = args[0].String.ToUpper();

            if (!Terminal.Shell.Commands.ContainsKey(commandName))
            {
                Terminal.Shell.IssueErrorMessage("Command {0} could not be found.", commandName);
                return;
            }

            var info = Terminal.Shell.Commands[commandName];

            if (info.help == null)
            {
                Terminal.Log("{0} does not provide any help documentation.", commandName);
            }
            else if (info.hint == null)
            {
                Terminal.Log(info.help);
            }
            else
            {
                Terminal.Log("{0}\nUsage: {1}", info.help, info.hint);
            }
        }

        [RegisterCommand(Help = "Time the execution of a command", MinArgCount = 1)]
        static void CommandTime(CommandArg[] args)
        {
            var sw = new Stopwatch();
            sw.Start();

            Terminal.Shell.RunCommand(JoinArguments(args));

            sw.Stop();
            Terminal.Log("Time: {0}ms", (double)sw.ElapsedTicks / 10000);
        }

        [RegisterCommand(Help = "Output message")]
        static void CommandPrint(CommandArg[] args)
        {
            Terminal.Log(JoinArguments(args));
        }

        // [RegisterCommand(Help = "List all variables or set a variable value")]
        // static void CommandSet(CommandArg[] args)
        // {
        //     if (args.Length == 0)
        //     {
        //         foreach (var kv in Terminal.Shell.Variables)
        //         {
        //             Terminal.Log("{0}: {1}", kv.Key.PadRight(16), kv.Value);
        //         }
        //         return;
        //     }

        //     string variableName = args[0].String;

        //     if (variableName[0] == '$')
        //     {
        //         Terminal.Log(TerminalLogType.Warning, "Warning: Variable name starts with '$', '${0}'.", variableName);
        //     }

        //     Terminal.Shell.SetVariable(variableName, JoinArguments(args, 1));
        // }

        // [RegisterCommand(Help = "No operation")]
        // static void CommandNoop(CommandArg[] args) { }

        //         [RegisterCommand(Help = "Quit running application", MaxArgCount = 0)]
        //         static void CommandQuit(CommandArg[] args)
        //         {
        // #if UNITY_EDITOR
        //             UnityEditor.EditorApplication.isPlaying = false;
        // #else
        //             Application.Quit();
        // #endif
        //         }

        static string JoinArguments(CommandArg[] args, int start = 0)
        {
            var sb = new StringBuilder();
            int arg_length = args.Length;

            for (int i = start; i < arg_length; i++)
            {
                sb.Append(args[i].String);

                if (i < arg_length - 1)
                {
                    sb.Append(" ");
                }
            }

            return sb.ToString();
        }
    }
}
