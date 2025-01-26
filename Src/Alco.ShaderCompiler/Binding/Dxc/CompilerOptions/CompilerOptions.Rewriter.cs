namespace DirectXShaderCompiler.NET;


public partial class CompilerOptions
{   
    /// <summary> Collect all global constants outside cbuffer declarations into cbuffer GlobalCB { ... }. Still experimental, not all dependency scenarios handled. </summary>
    [CompilerOption(name:"-decl-global-cb")]
    public bool createGlobalCBuffer = false; 

    /// <summary> Move uniform parameters from entry point to global scope </summary>
    [CompilerOption(name:"-extract-entry-uniforms")]
    public bool extractEntryUniforms = false; 
    
    /// <summary> Set extern on non-static globals </summary>
    [CompilerOption(name:"-global-extern-by-default")]
    public bool globalExternByDefault = false; 

    /// <summary> Write out user defines after rewritten HLSL </summary>
    [CompilerOption(name:"-keep-user-macro")]
    public bool keepUserMacro = false; 

    /// <summary> Add line directive </summary>
    [CompilerOption(name:"-line-directive")]
    public bool addLineDirective = false; 

    /// <summary> Remove unused functions and types </summary>
    [CompilerOption(name:"-remove-unused-functions")]
    public bool removeUnusedFunctions = false; 

    /// <summary> Remove unused static globals and functions </summary>
    [CompilerOption(name:"-remove-unused-globals")]
    public bool removeUnusedGlobals = false;
    
    /// <summary> Translate function definitions to declarations </summary>
    [CompilerOption(name:"-skip-fn-body")]
    public bool skipFunctionBody = false; 

    /// <summary> Remove static functions and globals when used with -skip-fn-body </summary>
    [CompilerOption(name:"-skip-static")]
    public bool skipStatic = false;
    
    /// <summary> Rewrite HLSL, without changes. </summary>
    [CompilerOption(name:"-unchanged")]
    public bool rewriteUnchanged = false; 
}