
using System.Text;

public class FileVector
{
    public static readonly string[] FieldsLowerCase = new string[] { "x", "y", "z", "w" };
    public static readonly string[] FieldsUpperCase = new string[] { "X", "Y", "Z", "W" };
    private readonly string _vectorType;
    private readonly int _vectorSize;
    public FileVector(string vectorType, int vectorSize)
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
        builder.AppendLine($"    public struct {_vectorType}{_vectorSize}");
        builder.AppendLine("    {");

        //fields
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.AppendLine($"        public {_vectorType} {FieldsLowerCase[i]};");
        }
        builder.AppendLine();

        //constructor single typed
        builder.AppendLine($"        public {_vectorType}{_vectorSize}({_vectorType} value)");
        builder.AppendLine("        {");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.AppendLine($"            this.{FieldsLowerCase[i]} = value;");
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
            builder.Append($"{_vectorType} {FieldsLowerCase[i]}");
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
            builder.AppendLine($"            this.{FieldsLowerCase[i]} = {FieldsLowerCase[i]};");
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
                    builder.Append($"{_vectorType} {FieldsLowerCase[j]}");
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
                    builder.AppendLine($"            this.{FieldsLowerCase[j]} = value.{FieldsLowerCase[j]};");
                }

                for (int j = lowerSize; j < _vectorSize; j++)
                {
                    builder.AppendLine($"            this.{FieldsLowerCase[j]} = {FieldsLowerCase[j]};");
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
            builder.Append($"a.{FieldsLowerCase[i]} + b.{FieldsLowerCase[i]}");
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
            builder.Append($"a.{FieldsLowerCase[i]} - b.{FieldsLowerCase[i]}");
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

        //operator *
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static {_vectorType}{_vectorSize} operator *({_vectorType}{_vectorSize} a, {_vectorType}{_vectorSize} b)");
        builder.AppendLine("        {");
        builder.Append($"            return new {_vectorType}{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"a.{FieldsLowerCase[i]} * b.{FieldsLowerCase[i]}");
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
            builder.Append($"a.{FieldsLowerCase[i]} / b.{FieldsLowerCase[i]}");
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

        //to vector
        builder.AppendLine();
        builder.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"        public static implicit operator Vector{_vectorSize}({_vectorType}{_vectorSize} a)");
        builder.AppendLine("        {");
        builder.Append($"            return new Vector{_vectorSize}(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"(float)a.{FieldsLowerCase[i]}");
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

        //to string
        builder.AppendLine();
        builder.AppendLine("        public override string ToString()");
        builder.AppendLine("        {");
        builder.Append("            return $\"(");
        for (int i = 0; i < _vectorSize; i++)
        {
            builder.Append($"{{{FieldsLowerCase[i]}}}");
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
                builder.AppendLine($"            this.{FieldsLowerCase[i]} = ({_vectorType})value;");
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
                builder.Append($"{type} {FieldsLowerCase[i]}");
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
                builder.AppendLine($"            this.{FieldsLowerCase[i]} = ({_vectorType}){FieldsLowerCase[i]};");
            }
            builder.AppendLine("        }");
            builder.AppendLine();
        }
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