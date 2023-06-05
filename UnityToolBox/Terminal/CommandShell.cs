using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityToolBox
{
    public struct CommandInfo
    {
        public Action<CommandArg[]> proc;
        public int maxArgCount;
        public int minArgCount;
        public string help;
        public string hint;
    }

    public struct CommandArg
    {
        public string String { get; set; }

        public int Int {
            get {
                int int_value;

                if (int.TryParse(String, out int_value)) {
                    return int_value;
                }

                TypeError("int");
                return 0;
            }
        }

        public float Float {
            get {
                float float_value;

                if (float.TryParse(String, out float_value)) {
                    return float_value;
                }

                TypeError("float");
                return 0;
            }
        }

        public bool Bool {
            get {
                if (string.Compare(String, "TRUE", ignoreCase: true) == 0) {
                    return true;
                }

                if (string.Compare(String, "FALSE", ignoreCase: true) == 0) {
                    return false;
                }

                TypeError("bool");
                return false;
            }
        }

        public override string ToString() {
            return String;
        }

        void TypeError(string expected_type) {
            Terminal.Shell.IssueErrorMessage(
                "Incorrect type for {0}, expected <{1}>",
                String, expected_type
            );
        }
    }

    public class CommandShell
    {
        Dictionary<string, CommandInfo> commands = new Dictionary<string, CommandInfo>();
        Dictionary<string, CommandArg> variables = new Dictionary<string, CommandArg>();
        List<CommandArg> arguments = new List<CommandArg>(); // Cache for performance

        public string IssuedErrorMessage { get; private set; }

        public Dictionary<string, CommandInfo> Commands {
            get { return commands; }
        }

        public Dictionary<string, CommandArg> Variables {
            get { return variables; }
        }

        public void RegisterCommands() {
            RegisterCommands(Assembly.GetCallingAssembly());
        }

        /// <summary>
        /// Uses reflection to find all RegisterCommand attributes
        /// and adds them to the commands dictionary.
        /// </summary>
        public void RegisterCommands(Assembly assembly) {
            var rejectedCommands = new Dictionary<string, CommandInfo>();
            var methodFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var type in assembly.GetTypes()) {
                foreach (var method in type.GetMethods(methodFlags)) {
                    var attribute = Attribute.GetCustomAttribute(
                        method, typeof(RegisterCommandAttribute)) as RegisterCommandAttribute;

                    if (attribute == null) {
                        if (method.Name.StartsWith("FRONTCOMMAND", StringComparison.CurrentCultureIgnoreCase)) {
                            // Front-end Command methods don't implement RegisterCommand, use default attribute
                            attribute = new RegisterCommandAttribute();
                        } else {
                            continue;
                        }
                    }

                    var methodsParams = method.GetParameters();

                    string commandName = InferFrontCommandName(method.Name);
                    Action<CommandArg[]> proc;

                    if (attribute.Name == null) {
                        // Use the method's name as the command's name
                        commandName = InferCommandName(commandName == null ? method.Name : commandName);
                    } else {
                        commandName = attribute.Name;
                    }

                   if (methodsParams.Length != 1 || methodsParams[0].ParameterType != typeof(CommandArg[])) {
                        // Method does not match expected Action signature,
                        // this could be a command that has a FrontCommand method to handle its arguments.
                        rejectedCommands.Add(commandName.ToUpper(), CommandFromParamInfo(methodsParams, attribute.Help));
                        continue;
                    }

                    // Convert MethodInfo to Action.
                    // This is essentially allows us to store a reference to the method,
                    // which makes calling the method significantly more performant than using MethodInfo.Invoke().
                    proc = (Action<CommandArg[]>)Delegate.CreateDelegate(typeof(Action<CommandArg[]>), method);
                    AddCommand(commandName, proc, attribute.MinArgCount, attribute.MaxArgCount, attribute.Help, attribute.Hint);
                }
            }
            HandleRejectedCommands(rejectedCommands);
        }

        /// <summary>
        /// Parses an input line into a command and runs that command.
        /// </summary>
        public void RunCommand(string line) {
            string remaining = line;
            IssuedErrorMessage = null;
            arguments.Clear();

            while (remaining != "") {
                var argument = EatArgument(ref remaining);

                if (argument.String != "") {
                    if (argument.String[0] == '$') {
                        string variable_name = argument.String.Substring(1).ToUpper();

                        if (variables.ContainsKey(variable_name)) {
                            // Replace variable argument if it's defined
                            argument = variables[variable_name];
                        }
                    }
                    arguments.Add(argument);
                }
            }

            if (arguments.Count == 0) {
                // Nothing to run
                return;
            }

            string command_name = arguments[0].String.ToUpper();
            arguments.RemoveAt(0); // Remove command name from arguments

            if (!commands.ContainsKey(command_name)) {
                IssueErrorMessage("Command {0} could not be found", command_name);
                return;
            }

            RunCommand(command_name, arguments.ToArray());
        }

        public void RunCommand(string command_name, CommandArg[] arguments) {
            var command = commands[command_name];
            int arg_count = arguments.Length;
            string error_message = null;
            int required_arg = 0;

            if (arg_count < command.minArgCount) {
                if (command.minArgCount == command.maxArgCount) {
                    error_message = "exactly";
                } else {
                    error_message = "at least";
                }
                required_arg = command.minArgCount;
            } else if (command.maxArgCount > -1 && arg_count > command.maxArgCount) {
                // Do not check max allowed number of arguments if it is -1
                if (command.minArgCount == command.maxArgCount) {
                    error_message = "exactly";
                } else {
                    error_message = "at most";
                }
                required_arg = command.maxArgCount;
            }

            if (error_message != null) {
                string plural_fix = required_arg == 1 ? "" : "s";

                IssueErrorMessage(
                    "{0} requires {1} {2} argument{3}",
                    command_name,
                    error_message,
                    required_arg,
                    plural_fix
                );

                if (command.hint != null) {
                    IssuedErrorMessage += string.Format("\n    -> Usage: {0}", command.hint);
                }

                return;
            }

            command.proc(arguments);
        }

        public void AddCommand(string name, CommandInfo info) {
            name = name.ToUpper();

            if (commands.ContainsKey(name)) {
                IssueErrorMessage("Command {0} is already defined.", name);
                return;
            }

            commands.Add(name, info);
        }

        public void AddCommand(string name, Action<CommandArg[]> proc, int minArgs = 0, int maxArgs = -1, string help = "", string hint = null) {
            var info = new CommandInfo() {
                proc = proc,
                minArgCount = minArgs,
                maxArgCount = maxArgs,
                help = help,
                hint = hint
            };

            AddCommand(name, info);
        }

        public void SetVariable(string name, string value) {
            SetVariable(name, new CommandArg() { String = value });
        }

        public void SetVariable(string name, CommandArg value) {
            name = name.ToUpper();

            if (variables.ContainsKey(name)) {
                variables[name] = value;
            } else {
                variables.Add(name, value);
            }
        }

        public CommandArg GetVariable(string name) {
            name = name.ToUpper();

            if (variables.ContainsKey(name)) {
                return variables[name];
            }

            IssueErrorMessage("No variable named {0}", name);
            return new CommandArg();
        }

        public void IssueErrorMessage(string format, params object[] message) {
            IssuedErrorMessage = string.Format(format, message);
        }

        string InferCommandName(string methodName) {
            string commandName;
            int index = methodName.IndexOf("COMMAND", StringComparison.CurrentCultureIgnoreCase);

            if (index >= 0) {
                // Method is prefixed, suffixed with, or contains "COMMAND".
                commandName = methodName.Remove(index, 7);
            } else {
                commandName = methodName;
            }

            return commandName;
        }

        string InferFrontCommandName(string methodName) {
            int index = methodName.IndexOf("FRONT", StringComparison.CurrentCultureIgnoreCase);
            return index >= 0 ? methodName.Remove(index, 5) : null;
        }

        void HandleRejectedCommands(Dictionary<string, CommandInfo> rejectedCommands) {
            foreach (var command in rejectedCommands) {
                if (commands.ContainsKey(command.Key)) {
                    commands[command.Key] = new CommandInfo() {
                        proc = commands[command.Key].proc,
                        minArgCount = command.Value.minArgCount,
                        maxArgCount = command.Value.maxArgCount,
                        help = command.Value.help
                    };
                } else {
                    IssueErrorMessage("{0} is missing a front command.", command);
                }
            }
        }

        CommandInfo CommandFromParamInfo(ParameterInfo[] parameters, string help) {
            int optionalArgs = 0;

            foreach (var param in parameters) {
                if (param.IsOptional) {
                    optionalArgs += 1;
                }
            }

            return new CommandInfo() {
                proc = null,
                minArgCount = parameters.Length - optionalArgs,
                maxArgCount = parameters.Length,
                help = help
            };
        }

        CommandArg EatArgument(ref string s) {
            var arg = new CommandArg();
            int spaceIndex = s.IndexOf(' ');

            if (spaceIndex >= 0) {
                arg.String = s.Substring(0, spaceIndex);
                s = s.Substring(spaceIndex + 1); // Remaining
            } else {
                arg.String = s;
                s = "";
            }

            return arg;
        }
    }
}
