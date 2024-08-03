namespace DirectXShaderCompiler.NET;

#pragma warning disable 1591

/// <summary> The denormal type DXC should use </summary>
public enum DenormalType { Any, Preserve, Ftz }

/// <summary> HLSL language versions consumed by DXC </summary>
public enum LanguageVersion { _2016, _2017, _2018, _2021 }

/// <summary> Library linkage type </summary>
public enum Linkage { Internal, External }

/// <summary> How DXC should handle flow control constructs </summary>
public enum FlowControlMode { Avoid, Prefer }

/// <summary> The debug info output type </summary>
public enum DebugInfoType { Normal, Slim }

/// <summary> Shader optimization level </summary>
public enum OptimizationLevel { O0, O1, O2, O3 }

/// <summary> How DXC should pack shader matrices </summary>
public enum MatrixPackMode { ColumnMajor, RowMajor }

/// <summary> How shaders should compute their signing hash </summary>
public enum HashComputationMode { FromSource, FromBinaryOnly }

#pragma warning restore 1591


/// <summary>
/// The options and settings available when compiling shaders.
/// </summary>
/// <remarks>
/// This class is primarily a wrapper around native DXC command line arguments, 
/// and therefore does not perform validation of certain input combinations that could break DXC and cause a segmentation fault.
/// </remarks>
public partial class CompilerOptions
{   
    /// <summary>
    /// Creates a new CompilerOptions instance with a given profile
    /// </summary>
    public CompilerOptions(ShaderProfile profile)
    {
        this.profile = profile;
    }

    /// <summary> Enables agressive flattening </summary>
    [CompilerOption(name:"-all-resources-bound")]
    public bool allResourcesBound = false; 
    
    /// <summary> Set auto binding space - enables auto resource binding in libraries </summary>
    [CompilerOption(name:"-auto-binding-space")]
    public string? autoBindingSpace = null; 
    
    /// <summary> Output color coded assembly listings </summary>
    [CompilerOption(name:"-Cc")]
    public bool outputColorCodedListings = false; 
    
    /// <summary> Set default linkage for non-shader functions when compiling or linking to a library target (internal, external) </summary>
    [CompilerOption(name:"-default-linkage")]
    public Linkage? defaultLinkage = null; 

    /// <summary> Select denormal value options (any, preserve, ftz). any is the default. </summary>
    [CompilerOption(name:"-denorm")]
    public DenormalType? denormalValue = null; 

    /// <summary> Disables/enables support for payload access qualifiers for raytracing payloads in SM 6.6/6.7. </summary>
    [CompilerOption(name:"-disable-payload-qualifiers", value:0)]
    [CompilerOption(name:"-enable-payload-qualifiers", value:1)]
    public bool? payloadQualifiers = null; 
    
    /// <summary> Enable 16bit types and disable min precision types. Available in HLSL 2018 and shader model 6.2 </summary>
    [CompilerOption(name:"-enable-16bit-types")]
    public bool enable16BitTypes = false;
    
    /// <summary> Enable generation of lifetime markers </summary>
    [CompilerOption(name:"-enable-lifetime-markers")]
    public bool enableLifetimeMarkers = false; 

    /// <summary> Set default encoding for source inputs and text outputs (utf8|utf16(win)|utf32(*nix)|wide) default=utf8 </summary>
    /// <remarks> Interop implementation details require encoding to be utf-8 only. As such, this option cannot be changed. </remarks>
    [CompilerOption(name:"-encoding")]
    public readonly string? encoding = "utf8"; 

    /// <summary> Only export shaders when compiling a library </summary>
    [CompilerOption(name:"-export-shaders-only")]
    public bool exportShadersOnly = false; 

    // NOTE: Not too sure how this is supposed to work- when I figure it out I'll make some wrappers to make building an export list easier 
    /// <summary> Specify exports when compiling a library: export1[[,export1_clone,...]=internal_name][;...] </summary>
    [CompilerOption(name:"-exports")]
    public string? exports = null; 
    
    /// <summary> Entry point name- defaults to main </summary>
    [CompilerOption(name:"-E")]
    public string entryPoint = "main"; 
    
    /// <summary> Output assembly code listing file </summary>
    [CompilerOption(name:"-Fc")]
    public string? assemblyListingFile = null; 

    /// <summary> Select diagnostic message format. Supported values: clang, msvc, mdvc-fallback, vi </summary>
    [CompilerOption(name:"-fdiagnostics-format", assignment:AssignmentType.Equals)]
    public string? diagnosticsFormat = null; 
    
    /// <summary> Print option name with mappable diagnostics </summary>
    [CompilerOption(name:"-fdiagnostics-show-option")]
    public bool diagnosticsShowOption = false; 

    /// <summary> Disable source location tracking in IR. This will break diagnostic generation for late validation. (Ignored if /Zi is passed) </summary>
    [CompilerOption(name:"-fdisable-loc-tracking")]
    public bool disableLocTracking = false; 
    
    /// <summary> Write debug information to the given file, or automatically named file in directory when ending in '\' </summary>
    [CompilerOption(name:"-Fd")]
    public string? debugOutputName = null; 

    /// <summary> Output warnings and errors to the given file </summary>
    [CompilerOption(name:"-Fe")]
    public string? errorOutputName = null;
    
    /// <summary> Output header file containing object code </summary>
    [CompilerOption(name:"-Fh")]
    public string? headerOutputName = null; 

    /// <summary> Set preprocess output file name (with /P) </summary>
    [CompilerOption(name:"-Fi")]
    public string? preprocessOutputName = null; 
    
    /// <summary> Expand the operands before performing token-pasting operation (fxc behavior) </summary>
    [CompilerOption(name:"-flegacy-macro-expansion")]
    public bool legacyExpandMacros = false; 

    /// <summary> Reserve unused explicit register assignments for compatibility with shader model 5.0 and below </summary>
    [CompilerOption(name:"-flegacy-resource-reservation")]
    public bool legacyResourceReservation = false; 

    /// <summary> Experimental option to use heuristics-driven late inlining and disable alwaysinline annotation for library shaders </summary>
    [CompilerOption(name:"-fnew-inlining-behavior")]
    public bool newInliningBehavior = false; 

    /// <summary> Do not print option name with mappable diagnostics </summary>
    [CompilerOption(name:"-fno-diagnostics-show-option")]
    public bool noDiagnosticsShowOption = false; 

    /// <summary> Force root signature version (rootsig_1_1 if omitted) </summary>
    [CompilerOption(name:"-force-rootsig-ver")]
    public string? rootsigVersion = null; 
    
    /// <summary> Output object file </summary>
    [CompilerOption(name:"-Fo")]
    public string? objectOutputFile = null; 

    /// <summary> Output reflection to the given file </summary>
    [CompilerOption(name:"-Fre")]
    public string? reflectionOutputFile = null; 

    /// <summary> Output root signature to the given file </summary>
    [CompilerOption(name:"-Frs")]
    public string? rootSignatureOutputFile = null; 

    /// <summary> Output shader hash to the given file </summary>
    [CompilerOption(name:"-Fsh")]
    public string? hashOutputFile = null; 

    /// <summary> Print time report </summary>
    [CompilerOption(name:"-ftime-report")]
    public bool timeReport = false;

    /// <summary> Minimum time granularity (in microseconds) traced by time profiler </summary>
    [CompilerOption(name:"-ftime-trace-granularity", Assignment = AssignmentType.Equals)]
    public int? timeTraceGranularity = null; 
    
    /// <summary> Print hierarchial time to file- stdout if no file is specified </summary>
    [CompilerOption(name:"-ftime-trace", Assignment = AssignmentType.Equals)]
    public string? timeTrace = null; 

    /// <summary> Enable backward compatibility mode </summary>
    [CompilerOption(name:"-Gec")]
    public bool backwardCompatibilityMode = false; 

    /// <summary> Enable strict mode </summary>
    [CompilerOption(name:"-Ges")]
    public bool strictMode = false; 

    /// <summary> How flow control constructs should be handled (prefer them or avoid them) </summary>
    [CompilerOption(name:"-Gfp", value:(int)FlowControlMode.Prefer)]
    [CompilerOption(name:"-Gfa", value:(int)FlowControlMode.Avoid)]
    public FlowControlMode? flowControlMode;

    /// <summary> Force IEEE strictness </summary>
    [CompilerOption(name:"-Gis")]
    public bool forceIEEEStrictness = false; 

    /// <summary> HLSL version (2016, 2017, 2018, 2021). Default is 2021 </summary>
    [CompilerOption(name:"-HV")]
    public LanguageVersion? languageVersion = LanguageVersion._2021; 

    /// <summary> Show header includes and nesting depth </summary>
    [CompilerOption(name:"-H")]
    public bool showIncludesAndNestingDepth = false; 
    
    /// <summary> Ignore line directives </summary>
    [CompilerOption(name:"-ignore-line-directives")]
    public bool ignoreLineDirectives = false; 
    
    /// <summary> Output hexadecimal literals </summary>
    [CompilerOption(name:"-Lx")]
    public bool outputHexLiterals = false; 

    /// <summary> Output instruction numbers in assembly listings </summary>
    [CompilerOption(name:"-Ni")]
    public bool outputInstructionNumbers = false; 

    /// <summary> Suppress warnings </summary>
    [CompilerOption(name:"-no-warnings")]
    public bool suppressWarnings = false; 

    /// <summary> Output instruction byte offsets in assembly listings </summary>
    [CompilerOption(name:"-No")]
    public bool outputByteOffsets = false; 

    /// <summary> Print the optimizer commands </summary>
    [CompilerOption(name:"-Odump")]
    public bool printOptimizerCommands = false; 
    
    /// <summary> Disable optimizations </summary>
    [CompilerOption(name:"-Od")]
    public bool disableOptimization = false; 

    /// <summary> Optimize signature packing assuming identical signature provided for each connecting stage </summary>
    [CompilerOption(name:"-pack-optimized")]
    public bool optimizeSignaturePacking = false; 
    
    /// <summary> (default) Pack signatures preserving prefix-stable property - appended elements will not disturb placement of prior elements </summary>
    [CompilerOption(name:"-pack-prefix-stable")]
    public bool packPrefixStable = false; 

    /// <summary> recompile from DXIL container with Debug Info or Debug Info bitcode file </summary>
    [CompilerOption(name:"-recompile")]
    public bool recompileFromDXIL = false; 

    /// <summary> Assume that UAVs/SRVs may alias </summary>
    [CompilerOption(name:"-res-may-alias")]
    public bool assumeResourceAliasing = false; 

    /// <summary> Read root signature from a #define </summary>
    [CompilerOption(name:"-rootsig-define")]
    public string? rootSignatureDefine = null;

    /// <summary> Target shader profile </summary>
    public ShaderProfile profile; 

    /// <summary> Disable validation </summary>
    [CompilerOption(name:"-Vd")]
    public bool disableValidation = false; 

    /// <summary> Verify diagnostic output using comment directives </summary>
    [CompilerOption(name:"-verify")]
    public string? verificationDirectives = null; 

    /// <summary> Display details about the include process. </summary>
    [CompilerOption(name:"-Vi")]
    public bool displayIncludeDetails = false; 

    /// <summary> Use as variable name in header file </summary>
    [CompilerOption(name:"-Vn")]
    public string? variableName = null; 
    
    /// <summary> Treat warnings as errors </summary>
    [CompilerOption(name:"-WX")]
    public bool warningsAsErrors = false; 

    /// <summary> Debug info type </summary>
    [CompilerOption(name:"-Zs", value:(int)DebugInfoType.Slim)]
    [CompilerOption(name:"-Zi", value:(int)DebugInfoType.Normal)]
    public DebugInfoType? debugInfo = null; 

    /// <summary> Should matrices be packed in column-major or row-major order </summary>
    [CompilerOption(name:"-Zpr", value:(int)MatrixPackMode.RowMajor)]
    [CompilerOption(name:"-Zpc", value:(int)MatrixPackMode.ColumnMajor)]
    public MatrixPackMode? matrixPackMode = null; 
    
    /// <summary> How the shader signing hash should be computed </summary>
    [CompilerOption(name:"-Zsb", value:(int)HashComputationMode.FromBinaryOnly)]
    [CompilerOption(name:"-Zss", value:(int)HashComputationMode.FromSource)]
    public HashComputationMode? hashComputationMode = null; 

    /// <summary> Force finite math only in shader </summary>
    [CompilerOption(name:"-fno-finite-math-only", value:0)]
    [CompilerOption(name:"-ffinite-math-only", value:1)]
    public bool? finiteMathOnly = null; 

    /// <summary> Optimization level </summary>
    [CompilerOption(name:"-O1", value:(int)OptimizationLevel.O1)]
    [CompilerOption(name:"-O0", value:(int)OptimizationLevel.O0)]
    [CompilerOption(name:"-O2", value:(int)OptimizationLevel.O2)]
    [CompilerOption(name:"-O3", value:(int)OptimizationLevel.O3)]
    public OptimizationLevel? optimization = OptimizationLevel.O3; 


    private Dictionary<string, string> macros = new();

    /// <summary> Define a macro </summary>
    public void SetMacro(string name, string value) => macros[name] = value; 

    /// <summary> Remove a macro </summary>
    public void RemoveMacro(string name) => macros.Remove(name); 

    private void AddMacros(List<string> args)
    {
        foreach (var macro in macros)
        {
            args.Add("-D");
            args.Add($"{macro.Key}={macro.Value}");
        }
    }


    private Dictionary<string, bool> warnings = new();
    
    /// <summary> Enable/disable a warning </summary>
    public void SetWarning(string name, bool value) => warnings[name] = value; 

    private void AddWarnings(List<string> args)
    {
        foreach (var warning in warnings)
        {
            string warnVal = warning.Value ? string.Empty : "no-";
            args.Add($"-W{warnVal}{warning.Key}");
        }
    }
}