using System.Reflection;

namespace DirectXShaderCompiler.NET;

public partial class CompilerOptions
{
    private enum AssignmentType { Equals, Spaced }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Field, AllowMultiple = true)]
    private class CompilerOptionAttribute : Attribute
    {
        public string Name;
        public int Value;
        public AssignmentType Assignment;

        public CompilerOptionAttribute(string name = "", int value = 0, AssignmentType assignment = AssignmentType.Spaced)
        {
            Name = name;
            Assignment = assignment;
            Value = value;
        }
    }

    // Cache reflection fields along with CompilerOptionAttribute
    private static readonly Dictionary<string, (FieldInfo, CompilerOptionAttribute[])> fields; 
    
    static CompilerOptions()
    {
        fields = [];
        foreach (FieldInfo fi in typeof(CompilerOptions).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            CompilerOptionAttribute[] attribs = fi.GetCustomAttributes<CompilerOptionAttribute>().ToArray();

            if (attribs.Length > 0)
                fields[fi.Name] = (fi, attribs);
        }
    }

    private static void SetBoolOption(List<string> args, CompilerOptionAttribute[] options, bool value)
    {
        // Enable/Disable compiler option based on boolean 
        if (options.Length < 2)
        {
            if (value)
                args.Add(options[0].Name);
            
            return;
        }

        // Set compiler option based on true/false
        if (value)
            args.Add(options[0].Value == 1 ? options[0].Name : options[1].Name);
        else
            args.Add(options[0].Value == 0 ? options[0].Name : options[1].Name);
    }   

    private static void SetStringOption(List<string> args, CompilerOptionAttribute option, string? str)
    {
        // String is empty but argument isn't null- add option only
        if (string.IsNullOrWhiteSpace(str))
        {
            args.Add(option.Name);
            return;  
        } 

        if (option.Assignment == AssignmentType.Equals)
        {
            args.Add($"{option.Name}={str.ToLower()}");
            return;
        }

        args.Add(option.Name);
        args.Add(str);
    }

    private static void SetEnumOption(List<string> args, CompilerOptionAttribute[] options, Enum enumValue)
    {
        // Only one option- pass enum as string value
        if (options.Length == 1)
        {
            SetStringOption(args, options[0], enumValue.ToString().Replace("_", ""));
            return;
        }

        // Find matching CompilerOption and add that to compiler args
        int enumVal = Convert.ToInt32(enumValue);
        CompilerOptionAttribute? matching = Array.Find(options, x => x.Value == enumVal);

        if (matching == null)
            return;
        
        args.Add(matching.Name);
    }

    private void AddOption(List<string> args, (FieldInfo, CompilerOptionAttribute[]) field)
    {
        object? nullableVal = field.Item1.GetValue(this);

        if (nullableVal == null)
            return;

        object value = nullableVal;

        if (value is bool boolValue)
            SetBoolOption(args, field.Item2, boolValue);
        else if (value is Enum enumValue)
            SetEnumOption(args, field.Item2, enumValue);
        else
            SetStringOption(args, field.Item2[0], value.ToString());
    }

    /// <summary>
    /// Get the options set in this <see cref="CompilerOptions"/> instance as a list of command-line arguments used by DXC
    /// </summary>
    public string[] GetArgumentsArray()
    {
        List<string> args = new List<string>();

        foreach (var pair in fields)
        {
            AddOption(args, pair.Value);
        }

        args.Add("-T");
        args.Add(ShaderProfileExtensions.ToString(profile));

        AddMacros(args);
        AddWarnings(args);
        AddSPIRVArgs(args);

        return args.ToArray();
    }
}