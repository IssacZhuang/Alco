namespace DirectXShaderCompiler.NET;

#pragma warning disable 1591

/// <summary>
/// Modification to a Vulkan descriptor set and binding
/// </summary>
public struct RegisterBinding
{
    public int typeNumber;
    public int space;
    public int binding;
    public int set;
}

/// <summary> Target Vulkan environments DXC supports </summary>
public enum TargetEnvironment
{
    Vulkan1_0,
    Vulkan1_1,
    Vulkan1_1Spirv1_4, 
    Vulkan1_2, 
    Vulkan1_3, 
    Universal1_5,
}

#pragma warning restore 1591

public partial class CompilerOptions
{   
    /// <summary> Specify whitelist of debug info category (file -> source -> line, tool, vulkan-with-source) </summary>
    [CompilerOption(name:"-fspv-debug", Assignment = AssignmentType.Equals)]
    public string? debugWhitelist = null; 

    /// <summary> Enables the MaximallyReconvergesKHR execution mode for this module. </summary>
    [CompilerOption(name:"-fspv-enable-maximal-reconvergence")]
    public bool? enableMaximalReconvergence = null; 
    
    /// <summary> Specify the SPIR-V entry point name. Defaults to the HLSL entry point name. </summary>
    [CompilerOption(name:"-fspv-entrypoint-name", Assignment = AssignmentType.Equals)]
    public string? entrypointName = null; 
    
    /// <summary> Specify SPIR-V extension permitted to use </summary>
    [CompilerOption(name:"-fspv-extension", Assignment = AssignmentType.Equals)]
    public string? extension = null; 

    /// <summary> Flatten arrays of resources so each array element takes one binding number </summary>
    [CompilerOption(name:"-fspv-flatten-resource-arrays")]               
    public bool flattenResourceArrays = false; 

    /// <summary> Set the maxumum value for an id in the SPIR-V binary. Default if 0x3FFFFF, which is the largest value all drivers must support. </summary>
    public (int, int)? maximumIDValue = null;

    /// <summary> Preserves all bindings declared within the module, even when those bindings are unused. </summary>
    [CompilerOption(name:"-fspv-preserve-bindings")]                     
    public bool preserveBindings = false; 

    /// <summary> Preserves all interface variables in the entry point, even when those variables are unused. </summary>
    [CompilerOption(name:"-fspv-preserve-interface")]                    
    public bool preserveInterface = false; 

    /// <summary> Print the SPIR-V module before each pass and after the last one. Useful for debugging SPIR-V legalization and optimization passes. </summary>
    [CompilerOption(name:"-fspv-print-all")]                             
    public bool printAll = false; 

    /// <summary> Replaces loads of composite objects to reduce memory pressure for the loads </summary>
    [CompilerOption(name:"-fspv-reduce-load-size")]                      
    public bool reduceLoadSize = false; 

    /// <summary> Emit additional SPIR-V instructions to aid reflection </summary>
    [CompilerOption(name:"-fspv-reflect")]                               
    public bool addReflectionAid = false; 

    /// <summary> Specify the target environment: vulkan1.0 (default), vulkan1.1, vulkan1.1spirv1.4, vulkan1.2, vulkan1.3, or universal1.5 </summary>
    public TargetEnvironment? targetEnvironment = null; 
    
    /// <summary> Assume the legacy matrix order (row major) when accessing raw buffers (e.g., ByteAdddressBuffer) </summary>
    [CompilerOption(name:"-fspv-use-legacy-buffer-matrix-order")]        
    public bool useLegacyBufferMatrixOrder = false; 

    /// <summary> Apply fvk-*-shift to resources without an explicit register assignment. </summary>
    [CompilerOption(name:"-fvk-auto-shift-bindings")]                    
    public bool autoShiftBindings = false; 

    /// <summary> Specify Vulkan binding number and set number for the $Globals cbuffer: 'binding' 'set' </summary>                         
    public (int, int)? globalBindingNumber = null; 

    /// <summary> Specify Vulkan descriptor set and binding for specific registers </summary>
    public List<RegisterBinding> registerBindings { get; private set; } = new(); 

    /// <summary> Negate SV_Position.y before writing to stage output in VS/DS/GS to accommodate Vulkan's coordinate system </summary>
    [CompilerOption(name:"-fvk-invert-y")]                               
    public bool invertY = false; 

    /// <summary> Follow Vulkan spec to use gl_BaseInstance as the first vertex instance, which makes SV_InstanceID = gl_InstanceIndex - gl_BaseInstance (without this option, SV_InstanceID = gl_InstanceIndex) </summary>
    [CompilerOption(name:"-fvk-support-nonzero-base-instance")]          
    public bool nonzeroBaseInstance = false; 

    /// <summary> Specify Vulkan binding number shift for b-type register: 'shift' 'space' </summary>                             
    public (int, int)? BRegisterBindingShift = null; 

    /// <summary> Specify Vulkan binding number shift for s-type register: 'shift' 'space' </summary>                               
    public (int, int)? SRegisterBindingShift = null; 
    
    /// <summary> Specify Vulkan binding number shift for t-type register: 'shift' 'space' </summary>                              
    public (int, int)? TRegisterBindingShift = null; 

    /// <summary> Specify Vulkan binding number shift for u-type register: 'shift' 'space' </summary>                              
    public (int, int)? URegisterBindingShift = null; 

    /// <summary> Use DirectX memory layout for Vulkan resources </summary>
    [CompilerOption(name:"-fvk-use-dx-layout")]                          
    public bool useDirectXMemoryLayout = false; 

    /// <summary> Reciprocate SV_Position.w after reading from stage input in PS to accommodate the difference between Vulkan and DirectX </summary>
    [CompilerOption(name:"-fvk-use-dx-position-w")]                      
    public bool useDirectXPositionW = false; 

    /// <summary> Use strict OpenGL std140/std430 memory layout for Vulkan resources </summary>
    [CompilerOption(name:"-fvk-use-gl-layout")]                          
    public bool useOpenGLMemoryLayout = false; 

    /// <summary> Use scalar memory layout for Vulkan resources </summary>
    [CompilerOption(name:"-fvk-use-scalar-layout")]                      
    public bool useScalarMemoryLayout = false; 

    /// <summary> Specify a comma-separated list of SPIRV-Tools passes to customize optimization configuration (see https://khr.io/hlsl2spirv#optimization) </summary>
    [CompilerOption(name:"-Oconfig", Assignment = AssignmentType.Equals)]
    public string? spirVOptimizationConfig = null; 

    /// <summary> Generate SPIR-V code </summary>
    [CompilerOption(name:"-spirv")]                                      
    public bool generateAsSpirV = false; 


    private void AddSPIRVArgs(List<string> args)
    {
        AddRegisterBindings(args);
        AddRegisterShifts(args);

        if (globalBindingNumber != null)
        {
            args.Add("-fvk-bind-globals");
            args.Add(globalBindingNumber.Value.Item1.ToString());
            args.Add(globalBindingNumber.Value.Item2.ToString());
        }

        if (maximumIDValue != null)
        {
            args.Add("-fspv-max-id");
            args.Add(maximumIDValue.Value.Item1.ToString());
            args.Add(maximumIDValue.Value.Item2.ToString());
        }

        if (targetEnvironment != null)
        {
            args.Add($"-fspv-target-env={targetEnvironment.Value.ToString().ToLower()}");
        }
    }


    private void AddRegisterBindings(List<string> args)
    {
        foreach (var registerBinding in registerBindings)
        {
            args.Add("-fvk-bind-register");
            args.Add(registerBinding.typeNumber.ToString());
            args.Add(registerBinding.space.ToString());
            args.Add(registerBinding.binding.ToString());
            args.Add(registerBinding.set.ToString());
        }
    }


    private void AddRegisterShifts(List<string> args)
    {
        void AddShift((int, int) shift, string name)
        {
            args.Add(name);
            args.Add(shift.Item1.ToString());
            args.Add(shift.Item2.ToString());
        }

        if (BRegisterBindingShift != null)
            AddShift(BRegisterBindingShift.Value, "-fvk-b-shift");

        if (SRegisterBindingShift != null)
            AddShift(SRegisterBindingShift.Value, "-fvk-s-shift");

        if (TRegisterBindingShift != null)
            AddShift(TRegisterBindingShift.Value, "-fvk-t-shift");
        
        if (URegisterBindingShift != null)
            AddShift(URegisterBindingShift.Value, "-fvk-u-shift");
    }
}