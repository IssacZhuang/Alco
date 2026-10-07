using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using Alco.Engine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Built-in agent tool for dynamically compiling and executing arbitrary C# scripts at
/// runtime, bound to an engine instance.
/// </summary>
/// <remarks>
/// Compilation runs on the agent (background) thread because it is CPU-bound and
/// touches no game state. The compiled script then executes on the engine main thread
/// so it can read game state directly (entities, maps, services) without callers
/// worrying about thread marshalling. Scripts may <c>await</c>: only the first
/// synchronous segment blocks the posted main-thread callback, and continuations are
/// pumped back onto the main thread by the engine loop once per frame, so the game
/// keeps running while a script waits.
/// </remarks>
public sealed class ScriptTool
{
    /// <summary>
    /// The engine whose main thread executes the compiled scripts.
    /// </summary>
    private readonly GameEngine _engine;

    /// <summary>
    /// Optional host-supplied factory producing the globals object injected into each
    /// script execution. Invoked once per <see cref="ExecuteScript"/> call on the
    /// calling (agent) thread. When null, or when it returns null, the default
    /// <see cref="ScriptGlobals"/> (which exposes the engine as <c>Engine</c>) is used.
    /// </summary>
    private readonly Func<object?>? _scriptGlobalsFactory;

    /// <summary>
    /// Default compilation timeout in milliseconds applied when the caller omits or
    /// supplies a non-positive <c>timeoutMs</c> argument. Guards the agent thread
    /// against runaway compilers.
    /// </summary>
    private readonly int _defaultTimeoutMs;

    /// <summary>
    /// Creates the tool bound to an engine.
    /// </summary>
    /// <param name="engine">The engine whose main thread executes scripts.</param>
    /// <param name="scriptGlobalsFactory">
    /// Optional factory invoked once per execution to build the script globals; its
    /// return type must be public with public members (a Roslyn scripting constraint,
    /// since scripts compile to a separate assembly). Return null from the factory to
    /// fall back to the default <see cref="ScriptGlobals"/> for that execution.
    /// </param>
    /// <param name="defaultTimeoutMs">Compilation timeout applied when the caller omits it.</param>
    public ScriptTool(GameEngine engine, Func<object?>? scriptGlobalsFactory = null, int defaultTimeoutMs = 10_000)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _scriptGlobalsFactory = scriptGlobalsFactory;
        _defaultTimeoutMs = defaultTimeoutMs > 0 ? defaultTimeoutMs : 10_000;
    }

    /// <summary>
    /// Compiles and executes an arbitrary C# script and returns its result as a string.
    /// Compilation runs on a background thread; execution runs on the engine main thread.
    /// The source is either the inline <paramref name="code"/> string or a .csx file at
    /// the absolute <paramref name="scriptPath"/> — provide exactly one of the two.
    /// </summary>
    /// <param name="code">The C# code to compile and execute. May use a top-level <c>return</c> statement; the return value is stringified.</param>
    /// <param name="scriptPath">
    /// Absolute path to a .csx script file to execute. Compilation diagnostics report positions
    /// in this file, and <c>#load</c>/<c>#r</c> directives inside it resolve relative paths
    /// against its directory.
    /// </param>
    /// <param name="timeoutMs">Compilation timeout in milliseconds. Defaults to the host's configured timeout when omitted or non-positive.</param>
    /// <returns>
    /// The stringified script return value on success, or a prefixed diagnostic message on
    /// compilation error (<c>Compilation error:</c>), runtime exception (<c>Runtime error:</c>),
    /// or compile timeout (<c>Compilation timed out</c>).
    /// </returns>
    [AgentFunction(IsOnAgentThread = true)]
    [Description(
        "Compile and execute an arbitrary C# script and return its stringified result. " +
        "Provide either inline 'code' or an absolute 'scriptPath' to a .csx file. " +
        "The script runs on the engine main thread with an 'Engine' global bound to the " +
        "engine instance, and may await tasks. The BCL namespaces (System, System.IO, " +
        "System.Linq, System.Collections.Generic, System.Globalization, System.Numerics, " +
        "System.Threading.Tasks), every loaded Alco.* namespace, and the host application's " +
        "root namespaces are imported by default. Use a top-level 'return' statement to yield " +
        "a value; compilation errors, runtime exceptions and timeouts are returned as prefixed " +
        "strings.")]
    public async Task<string> ExecuteScript(
        [Description("C# code to compile and execute. Provide either this or scriptPath. May use a top-level 'return' statement to yield a value.")] string code = "",
        [Description("Absolute path to a .csx script file to execute. Provide either this or code. Diagnostics report positions in the file; #load/#r resolve relative to its directory.")] string scriptPath = "",
        [Description("Compilation timeout in milliseconds. Defaults to the host's configured timeout.")] int timeoutMs = 0)
    {
        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(scriptPath))
        {
            return "Provide either 'code' or 'scriptPath', not both.";
        }

        ScriptOptions options;
        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            if (!Path.IsPathRooted(scriptPath))
            {
                return $"scriptPath must be an absolute path: '{scriptPath}'.";
            }

            if (!TryReadScriptFile(scriptPath, out code, out string? readError))
            {
                return readError;
            }

            string? baseDirectory = Path.GetDirectoryName(scriptPath);
            options = BuildScriptOptions()
                .WithFilePath(scriptPath)
                .WithSourceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, baseDirectory))
                .WithMetadataResolver(ScriptMetadataResolver.Default.WithBaseDirectory(baseDirectory));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "No code provided.";
            }

            options = BuildScriptOptions();
        }

        if (timeoutMs <= 0)
        {
            timeoutMs = _defaultTimeoutMs;
        }

        // Build the per-execution globals (the default exposes the engine as 'Engine').
        // The factory runs on this agent thread; the object itself crosses to the main
        // thread inside the runner below.
        object globals = _scriptGlobalsFactory?.Invoke() ?? new ScriptGlobals(_engine);

        // Compile on the background thread (CPU-bound, no game state). CompilationErrorException,
        // if any, surfaces here.
        ScriptRunner<object?>? runner;
        try
        {
            runner = await CompileAsync(code, timeoutMs, globals.GetType(), options).ConfigureAwait(false);
        }
        catch (CompilationErrorException ex)
        {
            return FormatCompilationError(ex);
        }
        catch (TimeoutException)
        {
            return $"Compilation timed out after {timeoutMs.ToString(CultureInfo.InvariantCulture)}ms.";
        }

        if (runner == null)
        {
            return "Compilation produced no entry point.";
        }

        // Execute on the engine main thread so the script body can read game state directly.
        // The posted lambda runs only the script's first synchronous segment and returns the
        // script task (Roslyn scripts are async); the engine main loop then pumps the script's
        // await continuations back onto the main thread once per frame. Blocking on the task
        // here would instead deadlock whenever a script awaits, because the continuation queue
        // is drained by that same main thread.
        object? returnValue;
        try
        {
            Task<object?> scriptTask = await _engine.PostToMainThreadAsync(() => runner(globals))
                .ConfigureAwait(false);
            returnValue = await scriptTask.ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return FormatRuntimeError(ex.InnerException);
        }
        catch (AggregateException ex)
        {
            return FormatRuntimeError(ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            return FormatRuntimeError(ex);
        }

        return FormatReturnValue(returnValue);
    }

    /// <summary>
    /// Compiles the given code into a reusable <see cref="ScriptRunner{T}"/> on the background thread,
    /// racing compilation against a timeout.
    /// </summary>
    /// <param name="code">The C# code to compile.</param>
    /// <param name="timeoutMs">Compilation timeout in milliseconds.</param>
    /// <param name="globalsType">
    /// The globals type whose public members the script sees as top-level names; must be
    /// public with public members because scripts compile to a separate assembly.
    /// </param>
    /// <param name="options">The script options carrying references, imports and, for file-based scripts, the source resolvers.</param>
    /// <returns>The compiled script runner, or <c>null</c> if the script has no entry point.</returns>
    /// <exception cref="CompilationErrorException">Thrown when the code fails to compile.</exception>
    /// <exception cref="TimeoutException">Thrown when compilation exceeds the timeout.</exception>
    private static async Task<ScriptRunner<object?>?> CompileAsync(string code, int timeoutMs, Type globalsType, ScriptOptions options)
    {
        Script<object?> script = CSharpScript.Create<object?>(code, options, globalsType);
        ScriptRunner<object?>? runner = script.CreateDelegate();

        // Force compilation (the heavy CPU work) and race it against the timeout. CreateDelegate
        // defers compilation lazily until first invocation, so we eagerly compile here to fail fast
        // on compile errors and to honour the timeout.
        Task compileTask = Task.Run(() => script.Compile());
        Task completed = await Task.WhenAny(compileTask, Task.Delay(timeoutMs)).ConfigureAwait(false);

        if (completed != compileTask)
        {
            throw new TimeoutException();
        }

        // Propagate any compilation exception synchronously to the caller.
        await compileTask.ConfigureAwait(false);
        return runner;
    }

    /// <summary>
    /// Builds the <see cref="ScriptOptions"/>: references every loaded assembly with an on-disk
    /// location so the script can reach all game and engine types, and imports the namespaces of all
    /// loaded Alco engine assemblies plus the host application's assemblies and the common BCL
    /// namespaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assemblies whose <see cref="Assembly.Location"/> is empty (dynamic or in-memory assemblies, and
    /// bundled assemblies under <c>PublishSingleFile</c>) are skipped for references: Roslyn cannot
    /// resolve metadata references for them via path, and framework assemblies are supplied by
    /// <see cref="ScriptOptions.Default"/> regardless. This keeps reference resolution robust across
    /// regular, single-file and trimmed builds without throwing.
    /// </para>
    /// <para>
    /// Imports are derived from loaded assemblies rather than hardcoded: every loaded Alco.* assembly
    /// contributes its assembly name as a namespace import (assembly name matches the root namespace
    /// by engine convention), and host application assemblies are imported when they actually contain
    /// a type in a namespace matching their assembly name — importing a namespace that does not exist
    /// would fail every script with an unresolved-namespace compile error. Design-time-only
    /// assemblies that are not loaded at runtime are never imported.
    /// </para>
    /// </remarks>
    /// <returns>The configured script options.</returns>
    private static ScriptOptions BuildScriptOptions()
    {
        var references = new List<MetadataReference>();
        var namespaces = new HashSet<string>(StringComparer.Ordinal)
        {
            // BCL namespaces always useful in scripts. System.Numerics supplies Vector2/int2 which are
            // pervasive across the engine APIs; System.IO and System.Threading.Tasks match the standard
            // dotnet-script/csi defaults so File/Task (and awaiting) work bare.
            "System",
            "System.IO",
            "System.Linq",
            "System.Collections.Generic",
            "System.Globalization",
            "System.Numerics",
            "System.Threading.Tasks",
        };

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            string name = assembly.GetName().Name ?? string.Empty;
            if (name.StartsWith("Alco", StringComparison.Ordinal) || HasRootNamespace(assembly, name))
            {
                namespaces.Add(name);
            }

            // IL3000 fires because under PublishSingleFile, Assembly.Location is empty. That is fine:
            // we skip empty-location assemblies below and rely on ScriptOptions.Default for framework
            // references. The read itself is benign and the fallback is intentional.
#pragma warning disable IL3000
            string location = assembly.Location;
#pragma warning restore IL3000
            if (string.IsNullOrEmpty(location))
            {
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
            catch (IOException)
            {
                // File vanished between the location check and the open; skip it.
            }
        }

        return ScriptOptions.Default
            .WithReferences(references)
            .WithImports(namespaces.OrderBy(ns => ns, StringComparer.Ordinal));
    }

    /// <summary>
    /// Reads a script file, mapping common IO failures to a prefixed error string.
    /// </summary>
    /// <param name="scriptPath">Absolute path to the script file.</param>
    /// <param name="code">The file contents when reading succeeds.</param>
    /// <param name="error">A prefixed error message when reading fails.</param>
    /// <returns>True when the file was read; false with <paramref name="error"/> set otherwise.</returns>
    private static bool TryReadScriptFile(string scriptPath, out string code, [NotNullWhen(false)] out string? error)
    {
        try
        {
            code = File.ReadAllText(scriptPath);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            code = string.Empty;
            error = $"Cannot read script file '{scriptPath}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Whether the assembly actually declares a type in a namespace matching its
    /// assembly name — the engine convention for importable root namespaces.
    /// </summary>
    private static bool HasRootNamespace(Assembly assembly, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // Framework assemblies are either covered by the BCL imports above or not
        // importable by convention; skip them without reflection cost.
        if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
            || name == "netstandard")
        {
            return false;
        }

        try
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.Namespace == name)
                {
                    return true;
                }
            }
        }
        catch (ReflectionTypeLoadException)
        {
            // Partially loadable assembly: treat as not importable.
        }

        return false;
    }

    /// <summary>
    /// Formats a script return value into the tool's string output.
    /// </summary>
    /// <param name="returnValue">The value returned by the script, which may be <c>null</c>.</param>
    /// <returns>The stringified value, or a placeholder when the script returned nothing.</returns>
    private static string FormatReturnValue(object? returnValue)
    {
        return returnValue switch
        {
            null => "Script executed. No return value.",
            _ => returnValue.ToString() ?? "Script returned null (ToString).",
        };
    }

    /// <summary>
    /// Formats a Roslyn compilation failure into a readable, prefixed diagnostic string.
    /// </summary>
    /// <param name="ex">The compilation exception carrying compiler diagnostics.</param>
    /// <returns>A multi-line string of compiler diagnostics prefixed with <c>Compilation error:</c>.</returns>
    private static string FormatCompilationError(CompilationErrorException ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Compilation error:");

        foreach (var diagnostic in ex.Diagnostics)
        {
            sb.Append("  ").Append(diagnostic.Severity).Append(' ')
                .Append(diagnostic.Id).Append(": ");

            // File-based scripts carry a source location; surface it (1-based, editor-style)
            // so diagnostics map back onto the .csx file. Inline submissions have no path.
            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            if (span.IsValid && !string.IsNullOrEmpty(span.Path))
            {
                sb.Append(span.Path)
                    .Append('(').Append(span.StartLinePosition.Line + 1).Append(',')
                    .Append(span.StartLinePosition.Character + 1).Append("): ");
            }

            sb.AppendLine(diagnostic.GetMessage());
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Formats a script runtime exception into a readable, prefixed diagnostic string.
    /// </summary>
    /// <param name="ex">The exception thrown while executing the compiled script.</param>
    /// <returns>A prefixed string with the exception type, message and stack trace.</returns>
    private static string FormatRuntimeError(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append("Runtime error: ")
          .Append(ex.GetType().Name).Append(": ")
          .Append(ex.Message);

        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine().Append(ex.StackTrace);
        }

        return sb.ToString();
    }
}
