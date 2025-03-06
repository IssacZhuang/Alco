
using System.Text;

public class FileMathInt
{
    public static readonly string[] FieldsLowerCase = new string[] { "x", "y", "z", "w" };
    public static readonly string[] FieldsUpperCase = new string[] { "X", "Y", "Z", "W" };
    private readonly string _vectorType;
    private readonly int _vectorSize;
    public FileMathInt(string vectorType, int vectorSize)
    {
        _vectorType = vectorType;
        _vectorSize = vectorSize;
    }

    public string GenerateContent()
    {
        StringBuilder builder = new StringBuilder();
        //namespace
        builder.AppendLine("//auto-generated");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Numerics;");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine();
        builder.AppendLine("namespace Alco");
        builder.AppendLine("{");

        //class
        builder.AppendLine($"    public static partial class math");
        builder.AppendLine("    {");

        //min
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} min({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"min(a.{FieldsUpperCase[i]}, b.{FieldsUpperCase[i]})");
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
            else
            {
                builder.AppendLine(");");
            }
        }
        builder.AppendLine("        }");
        builder.AppendLine();
        
        //max
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} max({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"max(a.{FieldsUpperCase[i]}, b.{FieldsUpperCase[i]})");
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
            else
            {
                builder.AppendLine(");");
            }
        }
        builder.AppendLine("        }");
        builder.AppendLine();

        //abs
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} abs({_vectorType}{_vectorSize} a)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"abs(a.{FieldsUpperCase[i]})");
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
            else
            {
                builder.AppendLine(");");
            }
        }
        builder.AppendLine("        }");
        builder.AppendLine();

        //select
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} select({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b, bool test)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return test ? b : a;");
        builder.AppendLine("        }");
        builder.AppendLine();

        //end class
        builder.AppendLine("    }");

        //end namespace
        builder.AppendLine("}");

        return builder.ToString();
    }

}




