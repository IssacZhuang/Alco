

public class GeneratorMath : BaseGenerator
{
    public override string OutputFolder => "Src/Alco/Math/~Generated";

    public override void Generate()
    {
        //del all files in folder
        ClearFolder();

        FileVector int2 = new FileVector("int", 2);
        FileVector int3 = new FileVector("int", 3);
        FileVector int4 = new FileVector("int", 4);

        FileMathInt mathInt2 = new FileMathInt("int", 2);
        FileMathInt mathInt3 = new FileMathInt("int", 3);
        FileMathInt mathInt4 = new FileMathInt("int", 4);

        FileVector uint2 = new FileVector("uint", 2);
        FileVector uint3 = new FileVector("uint", 3);
        FileVector uint4 = new FileVector("uint", 4);

        FileMathUint mathUint2 = new FileMathUint("uint", 2);
        FileMathUint mathUint3 = new FileMathUint("uint", 3);
        FileMathUint mathUint4 = new FileMathUint("uint", 4);

        FileVector half2 = new FileVector("Half", 2);
        FileVector half3 = new FileVector("Half", 3);
        FileVector half4 = new FileVector("Half", 4);

        FileMathHalf mathHalf2 = new FileMathHalf("Half", 2);
        FileMathHalf mathHalf3 = new FileMathHalf("Half", 3);
        FileMathHalf mathHalf4 = new FileMathHalf("Half", 4);
        


        WriteFile("int2.gen.cs", int2.GenerateContent());
        WriteFile("int3.gen.cs", int3.GenerateContent());
        WriteFile("int4.gen.cs", int4.GenerateContent());


        WriteFile("MathInt2.gen.cs", mathInt2.GenerateContent());
        WriteFile("MathInt3.gen.cs", mathInt3.GenerateContent());
        WriteFile("MathInt4.gen.cs", mathInt4.GenerateContent());
        

        WriteFile("uint2.gen.cs", uint2.GenerateContent());
        WriteFile("uint3.gen.cs", uint3.GenerateContent());
        WriteFile("uint4.gen.cs", uint4.GenerateContent());

        WriteFile("MathUint2.gen.cs", mathUint2.GenerateContent());
        WriteFile("MathUint3.gen.cs", mathUint3.GenerateContent());
        WriteFile("MathUint4.gen.cs", mathUint4.GenerateContent());

        WriteFile("Half2.gen.cs", half2.GenerateContent());
        WriteFile("Half3.gen.cs", half3.GenerateContent());
        WriteFile("Half4.gen.cs", half4.GenerateContent());

        WriteFile("MathHalf2.gen.cs", mathHalf2.GenerateContent());
        WriteFile("MathHalf3.gen.cs", mathHalf3.GenerateContent());
        WriteFile("MathHalf4.gen.cs", mathHalf4.GenerateContent());

    }
}