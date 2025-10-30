
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
        //namespace
        builder.AppendLine("//auto-generated");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Numerics;");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine();
        builder.AppendLine("namespace Alco");
        builder.AppendLine("{");

        //class
        builder.AppendLine($"    public struct {_vectorType}{_vectorSize}");
        builder.AppendLine("    {");

        //fields
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.AppendLine($"        public {_vectorType} {FieldsUpperCase[i]};");
        }
        builder.AppendLine();

        //static constants
        AppendStaticConstants(builder);

        //constructor single typed
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

        //constructor full typed
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

        //constructor lower sized vector
        //like: public Vector4(Vector3 value, float w);
        //or: public Vector4(Vector2 value, float z, float w);
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

        //operator +
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

        //operator -
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

        //operator - (unary)
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

        //operator *
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

        //operator /
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

        //operator ==
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

        //operator !=
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static bool operator !=({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.AppendLine("            return !(a == b);");
        builder.AppendLine("        }");

        //to vector
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

        //to type
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

        //equals
        builder.AppendLine();
        builder.AppendLine("        public override bool Equals(object obj)");
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

        //to string
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

        //end class
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
        // Zero constant - all components are 0
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

        // One constant - all components are 1
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

        // Unit vectors
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

//template

// using System;
// using System.Numerics;
// using System.Runtime.CompilerServices;

// namespace Alco
// {
//     public struct int2
//     {
//         public int x;
//         public int y;

//         public int2(int value)
//         {
//             this.x = value;
//             this.y = value;
//         }

//         public int2(int x, int y)
//         {
//             this.x = x;
//             this.y = y;
//         }

//         public static int2 operator +(int2 a, int2 b)
//         {
//             return new int2(a.x + b.x, a.y + b.y);
//         }

//         public static int2 operator -(int2 a, int2 b)
//         {
//             return new int2(a.x - b.x, a.y - b.y);
//         }

//         public static int2 operator *(int2 a, int2 b)
//         {
//             return new int2(a.x * b.x, a.y * b.y);
//         }

//         public static int2 operator /(int2 a, int2 b)
//         {
//             return new int2(a.x / b.x, a.y / b.y);
//         }

//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public static bool operator ==(int2 a, int2 b)
//         {
//             return a.x == b.x && a.y == b.y;
//         }

//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public static bool operator !=(int2 a, int2 b)
//         {
//             return !(a == b);
//         }

//         public override bool Equals(object obj)
//         {
//             return obj is int2 other && this == other;
//         }

//         public bool Equals(int2 other)
//         {
//             return this == other;
//         }

//         public override int GetHashCode()
//         {
//             return HashCode.Combine(x, y);
//         }

//         //to vector2

//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public static implicit operator Vector2(int2 a)
//         {
//             return new System.Numerics.Vector2(a.x, a.y);
//         }

//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         public static implicit operator int2(Vector2 a)
//         {
//             return new int2((int)a.X, (int)a.Y);
//         }

//         public override string ToString()
//         {
//             return $"({x}, {y})";
//         }
//     }
// }