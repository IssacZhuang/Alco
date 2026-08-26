
using System.Text;

public class FileVector
{
    public static readonly string[] FieldsLowerCase = new string[] { "x", "y", "z", "w" };
    public static readonly string[] FieldsUpperCase = new string[] { "X", "Y", "Z", "W" };
    private readonly string _vectorType;
    private readonly int _vectorSize;
    private readonly bool _isSigned;
    public FileVector(string vectorType, int vectorSize, bool isSigned = false)
    {
        _vectorType = vectorType;
        _vectorSize = vectorSize;
        _isSigned = isSigned;
    }

    public string GenerateContent()
    {
        StringBuilder builder = new StringBuilder();
        // Emits the complete C# source of a vector struct: fields, constants, constructors, operators and conversions.
        builder.AppendLine("//auto-generated");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Numerics;");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine();
        builder.AppendLine("namespace Alco");
        builder.AppendLine("{");

        builder.AppendLine($"    public struct {_vectorType}{_vectorSize} : IEquatable<{_vectorType}{_vectorSize}>");
        builder.AppendLine("    {");

        for (int i = 0; i < _vectorSize; i++)
        {
            builder.AppendLine($"        public {_vectorType} {FieldsUpperCase[i]};");
        }
        builder.AppendLine();

        AppendStaticConstants(builder);

        builder.AppendLine($"        public {_vectorType}{_vectorSize}({_vectorType} value)");
        builder.AppendLine("        {");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.AppendLine($"            this.{FieldsUpperCase[i]} = value;");
        }
        builder.AppendLine("        }");
        builder.AppendLine();

        AppendConstructorSingle(builder, "int");
        AppendConstructorSingle(builder, "uint");
        AppendConstructorSingle(builder, "float");

        builder.Append($"        public {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"{_vectorType} {FieldsUpperCase[i]}");
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
            else
            {
                builder.AppendLine(")");
            }
        }
        builder.AppendLine("        {");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.AppendLine($"            this.{FieldsUpperCase[i]} = {FieldsUpperCase[i]};");
        }
        builder.AppendLine("        }");
        builder.AppendLine();

        AppendConstructorFull(builder, "int");
        AppendConstructorFull(builder, "uint");
        AppendConstructorFull(builder, "float");

        if (_vectorSize >= 3)
        {
            for (int i = 3; i <= _vectorSize; i++)
            {
                int lowerSize = i - 1;
                builder.Append($"        public {_vectorType}{_vectorSize}(");
                builder.Append($"{_vectorType}{lowerSize} value, ");
                for (int j = lowerSize; j < _vectorSize; j++)
                {
                    builder.Append($"{_vectorType} {FieldsUpperCase[j]}");
                    if (j < _vectorSize - 1)
                    {
                        builder.Append(", ");
                    }
                    else
                    {
                        builder.AppendLine(")");
                    }
                }
                builder.AppendLine("        {");
                for (int j = 0; j < lowerSize; j++)
                {
                    builder.AppendLine($"            this.{FieldsUpperCase[j]} = value.{FieldsUpperCase[j]};");
                }

                for (int j = lowerSize; j < _vectorSize; j++)
                {
                    builder.AppendLine($"            this.{FieldsUpperCase[j]} = {FieldsUpperCase[j]};");
                }
                builder.AppendLine("        }");
                builder.AppendLine();
            }
        }

        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} operator +({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"a.{FieldsUpperCase[i]} + b.{FieldsUpperCase[i]}");
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

        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} operator -({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"a.{FieldsUpperCase[i]} - b.{FieldsUpperCase[i]}");
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

        if (_isSigned)
        {
            builder.AppendLine($"        /// <summary>");
            builder.AppendLine($"        /// Negates the specified {_vectorType}{_vectorSize} value.");
            builder.AppendLine($"        /// </summary>");
            builder.AppendLine($"        /// <param name=\"a\">The value to negate.</param>");
            builder.AppendLine($"        /// <returns>A new {_vectorType}{_vectorSize} with all components negated.</returns>");
            builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            builder.AppendLine($"        public static {_vectorType}{_vectorSize} operator -({_vectorType}{_vectorSize} a)");
            builder.AppendLine("        {");
            builder.Append($"            return new {_vectorType}{_vectorSize}(");
            for (int i = 0; i < _vectorSize; i++)
            {
                builder.Append($"-a.{FieldsUpperCase[i]}");
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
        }

        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} operator *({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"a.{FieldsUpperCase[i]} * b.{FieldsUpperCase[i]}");
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

        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} operator /({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"a.{FieldsUpperCase[i]} / b.{FieldsUpperCase[i]}");
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

        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static bool operator ==({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append("            return ");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"a.{FieldsUpperCase[i]} == b.{FieldsUpperCase[i]}");
            if (i < _vectorSize - 1)
            {
                builder.Append(" && ");
            }
            else
            {
                builder.AppendLine(";");
            }
        }
        builder.AppendLine("        }");

        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static bool operator !=({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.AppendLine("            return !(a == b);");
        builder.AppendLine("        }");

        builder.AppendLine();
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static implicit operator Vector{_vectorSize}({_vectorType}{_vectorSize} a)");
        builder.AppendLine("        {");
        builder.Append($"            return new Vector{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"(float)a.{FieldsUpperCase[i]}");
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
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static implicit operator {_vectorType}{_vectorSize}(Vector{_vectorSize} a)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"({_vectorType})a.{FieldsUpperCase[i]}");
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
        builder.AppendLine("        public override bool Equals(object? obj)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return obj is {_vectorType}{_vectorSize} other && this == other;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public bool Equals({_vectorType}{_vectorSize} other)");
        builder.AppendLine("        {");
        builder.AppendLine("            return this == other;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public override int GetHashCode()");
        builder.AppendLine("        {");
        builder.Append("            return HashCode.Combine(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"{FieldsUpperCase[i]}");
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
        builder.AppendLine("        public override string ToString()");
        builder.AppendLine("        {");
        builder.Append("            return $\"(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"{{{FieldsUpperCase[i]}}}");
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
            else
            {
                builder.AppendLine(")\";");
            }
        }
        builder.AppendLine("        }");

        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private void AppendConstructorSingle(StringBuilder builder, string type){
        if (_vectorType != type)
        {
            builder.AppendLine($"        public {_vectorType}{_vectorSize}({type} value)");
            builder.AppendLine("        {");
            for (int i = 0; i < _vectorSize; i++)
            {
                builder.AppendLine($"            this.{FieldsUpperCase[i]} = ({_vectorType})value;");
            }
            builder.AppendLine("        }");
            builder.AppendLine();
        }
    }


    private void AppendConstructorFull(StringBuilder builder, string type){
        if (_vectorType != type)
        {
            builder.Append($"        public {_vectorType}{_vectorSize}(");
            for (int i = 0; i < _vectorSize; i++)
            {
                builder.Append($"{type} {FieldsUpperCase[i]}");
                if (i < _vectorSize - 1)
                {
                    builder.Append(", ");
                }
                else
                {
                    builder.AppendLine(")");
                }
            }
            builder.AppendLine("        {");
            for (int i = 0; i < _vectorSize; i++)
            {
                builder.AppendLine($"            this.{FieldsUpperCase[i]} = ({_vectorType}){FieldsUpperCase[i]};");
            }
            builder.AppendLine("        }");
            builder.AppendLine();
        }
    }

    private void AppendStaticConstants(StringBuilder builder)
    {
        builder.AppendLine("        /// <summary>");
        builder.AppendLine($"        /// A {_vectorType}{_vectorSize} with all components set to zero.");
        builder.AppendLine("        /// </summary>");
        builder.Append($"        public static readonly {_vectorType}{_vectorSize} Zero = new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            string zeroValue = GetZeroValue();
            builder.Append(zeroValue);
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
        }
        builder.AppendLine(");");
        builder.AppendLine();

        builder.AppendLine("        /// <summary>");
        builder.AppendLine($"        /// A {_vectorType}{_vectorSize} with all components set to one.");
        builder.AppendLine("        /// </summary>");
        builder.Append($"        public static readonly {_vectorType}{_vectorSize} One = new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            string oneValue = GetOneValue();
            builder.Append(oneValue);
            if (i < _vectorSize - 1)
            {
                builder.Append(", ");
            }
        }
        builder.AppendLine(");");
        builder.AppendLine();

        for (int unitIndex = 0; unitIndex < _vectorSize; unitIndex++)
        {
            string unitName = GetUnitName(unitIndex);
            string componentName = FieldsUpperCase[unitIndex];

            builder.AppendLine("        /// <summary>");
            builder.AppendLine($"        /// A unit vector with the {componentName} component set to one and all other components set to zero.");
            builder.AppendLine("        /// </summary>");
            builder.Append($"        public static readonly {_vectorType}{_vectorSize} {unitName} = new {_vectorType}{_vectorSize}(");

            for (int i = 0; i < _vectorSize; i++)
            {
                string value = (i == unitIndex) ? GetOneValue() : GetZeroValue();
                builder.Append(value);
                if (i < _vectorSize - 1)
                {
                    builder.Append(", ");
                }
            }
            builder.AppendLine(");");
            builder.AppendLine();
        }
    }

    private string GetZeroValue()
    {
        return _vectorType switch
        {
            "int" => "0",
            "uint" => "0u",
            "float" => "0.0f",
            "double" => "0.0",
            _ => "0"
        };
    }

    private string GetOneValue()
    {
        return _vectorType switch
        {
            "int" => "1",
            "uint" => "1u",
            "float" => "1.0f",
            "double" => "1.0",
            _ => "1"
        };
    }

    private string GetUnitName(int index)
    {
        return index switch
        {
            0 => "UnitX",
            1 => "UnitY",
            2 => "UnitZ",
            3 => "UnitW",
            _ => $"Unit{FieldsUpperCase[index]}"
        };
    }
}