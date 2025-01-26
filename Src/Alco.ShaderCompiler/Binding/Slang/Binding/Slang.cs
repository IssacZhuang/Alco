using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{
    public const string COMPILER_OPTION_PRESERVE_PARAMETERS = "-preserve-params";
    // const Option generalOpts[] =
    //     {
    //         { OptionKind::MacroDefine,  "-D?...",   "-D<name>[=<value>], -D <name>[=<value>]",
    //         "Insert a preprocessor macro.\n"
    //         "The space between - D and <name> is optional. If no <value> is specified, Slang will define the macro with an empty value." },
    //         { OptionKind::DepFile,      "-depfile", "-depfile <path>", "Save the source file dependency list in a file." },
    //         { OptionKind::EntryPointName, "-entry", "-entry <name>",
    //         "Specify the name of an entry-point function.\n"
    //         "When compiling from a single file, this defaults to main if you specify a stage using -stage.\n"
    //         "Multiple -entry options may be used in a single invocation. "
    //         "When they do, the file associated with the entry point will be the first one found when searching to the left in the command line.\n"
    //         "If no -entry options are given, compiler will use [shader(...)] "
    //         "attributes to detect entry points."},
    //         { OptionKind::Specialize, "-specialize", "-specialize <typename>",
    //             "Specialize the last entrypoint with <typename>.\n"},
    //         { OptionKind::EmitIr,       "-emit-ir", nullptr, "Emit IR typically as a '.slang-module' when outputting to a container." },
    //         { OptionKind::Help,         "-h,-help,--help", "-h or -h <help-category>", "Print this message, or help in specified category." },
    //         { OptionKind::HelpStyle,    "-help-style", "-help-style <help-style>", "Help formatting style" },
    //         { OptionKind::Include,      "-I?...", "-I<path>, -I <path>",
    //         "Add a path to be used in resolving '#include' "
    //         "and 'import' operations."},
    //         { OptionKind::Language,     "-lang", "-lang <language>", "Set the language for the following input files."},
    //         { OptionKind::MatrixLayoutColumn, "-matrix-layout-column-major", nullptr, "Set the default matrix layout to column-major."},
    //         { OptionKind::MatrixLayoutRow,"-matrix-layout-row-major", nullptr, "Set the default matrix layout to row-major."},
    //         { OptionKind::RestrictiveCapabilityCheck,"-restrictive-capability-check", nullptr, "Many capability warnings will become an error."},
    //         { OptionKind::ZeroInitialize, "-zero-initialize", nullptr,
    //         "Initialize all variables to zero."
    //         "Structs will set all struct-fields without an init expression to 0."
    //         "All variables will call their default constructor if not explicitly initialized as usual."},
    //         { OptionKind::IgnoreCapabilities,"-ignore-capabilities", nullptr, "Do not warn or error if capabilities are violated"},
    //         { OptionKind::MinimumSlangOptimization, "-minimum-slang-optimization", nullptr, "Perform minimum code optimization in Slang to favor compilation time."},
    //         { OptionKind::DisableNonEssentialValidations, "-disable-non-essential-validations", nullptr, "Disable non-essential IR validations such as use of uninitialized variables."},
    //         { OptionKind::DisableSourceMap, "-disable-source-map", nullptr, "Disable source mapping in the Obfuscation."},
    //         { OptionKind::ModuleName,     "-module-name", "-module-name <name>",
    //         "Set the module name to use when compiling multiple .slang source files into a single module."},
    //         { OptionKind::Output, "-o", "-o <path>",
    //         "Specify a path where generated output should be written.\n"
    //         "If no -target or -stage is specified, one may be inferred "
    //         "from file extension (see <file-extension>). "
    //         "If multiple -target options and a single -entry are present, each -o "
    //         "associates with the first -target to its left. "
    //         "Otherwise, if multiple -entry options are present, each -o associates "
    //         "with the first -entry to its left, and with the -target that matches "
    //         "the one inferred from <path>."},
    //         { OptionKind::Profile, "-profile", "-profile <profile>[+<capability>...]",
    //         "Specify the shader profile for code generation.\n"
    //         "Accepted profiles are:\n"
    //         "* sm_{4_0,4_1,5_0,5_1,6_0,6_1,6_2,6_3,6_4,6_5,6_6}\n"
    //         "* glsl_{110,120,130,140,150,330,400,410,420,430,440,450,460}\n"
    //         "Additional profiles that include -stage information:\n"
    //         "* {vs,hs,ds,gs,ps}_<version>\n"
    //         "See -capability for information on <capability>\n"
    //         "When multiple -target options are present, each -profile associates "
    //         "with the first -target to its left."},
    //         { OptionKind::Stage, "-stage", "-stage <stage>",
    //         "Specify the stage of an entry-point function.\n"
    //         "When multiple -entry options are present, each -stage associated with "
    //         "the first -entry to its left.\n"
    //         "May be omitted if entry-point function has a [shader(...)] attribute; "
    //         "otherwise required for each -entry option."},
    //         { OptionKind::Target, "-target", "-target <target>", "Specifies the format in which code should be generated."},
    //         { OptionKind::Version, "-v,-version", nullptr,
    //             "Display the build version. This is the contents of git describe --tags.\n"
    //             "It is typically only set from automated builds(such as distros available on github).A user build will by default be 'unknown'."},
    //         { OptionKind::WarningsAsErrors, "-warnings-as-errors", "-warnings-as-errors all or -warnings-as-errors <id>[,<id>...]",
    //         "all - Treat all warnings as errors.\n"
    //         "<id>[,<id>...]: Treat specific warning ids as errors.\n"},
    //         { OptionKind::DisableWarnings, "-warnings-disable", "-warnings-disable <id>[,<id>...]", "Disable specific warning ids."},
    //         { OptionKind::EnableWarning, "-W...", "-W<id>", "Enable a warning with the specified id."},
    //         { OptionKind::DisableWarning, "-Wno-...", "-Wno-<id>", "Disable warning with <id>"},
    //         { OptionKind::DumpWarningDiagnostics, "-dump-warning-diagnostics", nullptr, "Dump to output list of warning diagnostic numeric and name ids." },
    //         { OptionKind::InputFilesRemain, "--", nullptr, "Treat the rest of the command line as input files."},
    //         { OptionKind::ReportDownstreamTime, "-report-downstream-time", nullptr, "Reports the time spent in the downstream compiler." },
    //         { OptionKind::ReportPerfBenchmark, "-report-perf-benchmark", nullptr, "Reports compiler performance benchmark results." },
    //         { OptionKind::SkipSPIRVValidation, "-skip-spirv-validation", nullptr, "Skips spirv validation." },
    //         { OptionKind::SourceEmbedStyle, "-source-embed-style", "-source-embed-style <source-embed-style>",
    //         "If source embedding is enabled, defines the style used. When enabled (with any style other than `none`), "
    //         "will write compile results into embeddable source for the target language. "
    //         "If no output file is specified, the output is written to stdout. If an output file is specified "
    //         "it is written either to that file directly (if it is appropriate for the target language), "
    //         "or it will be output to the filename with an appropriate extension.\n\n"
    //         "Note for C/C++ with u16/u32/u64 types it is necessary to have \"#include <stdint.h>\" before the generated file.\n" },
    //         { OptionKind::SourceEmbedName, "-source-embed-name", "-source-embed-name <name>",
    //         "The name used as the basis for variables output for source embedding."},
    //         { OptionKind::SourceEmbedLanguage, "-source-embed-language", "-source-embed-language <language>",
    //         "The language to be used for source embedding. Defaults to C/C++. Currently only C/C++ are supported"},
    //         { OptionKind::DisableShortCircuit, "-disable-short-circuit", nullptr, "Disable short-circuiting for \"&&\" and \"||\" operations" },
    //         { OptionKind::UnscopedEnum, "-unscoped-enum", nullptr, "Treat enums types as unscoped by default."},
    //         { OptionKind::PreserveParameters, "-preserve-params", nullptr, "Preserve all resource parameters in the output code, even if they are not used by the shader."}
    //     };

}