namespace DirectXShaderCompiler.NET;

public partial class CompilerOptions
{   
    /// <summary> Load a binary file rather than compiling </summary>
    [CompilerOption(name:"-dumpbin")]
    public bool dumpBin = false; 

    /// <summary> Extract root signature from shader bytecode (must be used with /Fo 'file') </summary>
    [CompilerOption(name:"-extractrootsignature")]                       
    public bool extractRootSignature = false; 
    
    /// <summary> Save private data from shader blob </summary>
    [CompilerOption(name:"-getprivate")] 
    public string? privateDataFile = null; 
    
    /// <summary> Link list of libraries provided in 'inputs' argument separated by ';' </summary>
    [CompilerOption(name:"-link")] 
    public bool? link = null; 

    /// <summary> Preprocess to file </summary>
    [CompilerOption(name:"-P")]                                          
    public bool outputPreprocessedCode = false; 

    /// <summary> Embed PDB in shader container (must be used with /Zi) </summary>
    [CompilerOption(name:"-Qembed_debug")]                               
    public bool embedDebugPDB = false; 

    /// <summary> Embed source code in PDB </summary>
    [CompilerOption(name:"-Qsource_in_debug_module")]
    public bool embedSourcePDB = false;      
    
    /// <summary> Strip debug information from 4_0+ shader bytecode  (must be used with /Fo 'file') </summary>
    [CompilerOption(name:"-Qstrip_debug")]                               
    public bool stripDebugPDB = false; 
    
    /// <summary> Strip private data from shader bytecode  (must be used with /Fo 'file') </summary>
    [CompilerOption(name:"-Qstrip_priv")]                                
    public bool stripPrivate = false; 
    
    /// <summary> Strip reflection data from shader bytecode  (must be used with /Fo 'file') </summary>
    [CompilerOption(name:"-Qstrip_reflect")]                             
    public bool stripReflection = false; 
    
    /// <summary> Strip root signature data from shader bytecode  (must be used with /Fo 'file') </summary>
    [CompilerOption(name:"-Qstrip_rootsignature")]                       
    public bool stripRootSignature = false; 
    
    /// <summary> Private data to add to compiled shader blob </summary>
    [CompilerOption(name:"-setprivate")]                    
    public string? additionalPrivateData = null; 
    
    /// <summary> Attach root signature to shader bytecode </summary>
    [CompilerOption(name:"-setrootsignature")]                   
    public string? attachedRootSignature = null; 
    
    /// <summary> Verify shader bytecode with root signature </summary>
    [CompilerOption(name:"-verifyrootsignature")]                 
    public string? verifyRootSignature = null; 
}