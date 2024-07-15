

public class GeneratorMath : BaseGenerator
{
    public override string OutputFolder => "Src/Vocore/Math/~Generated";

    public override void Generate()
    {
        //del all files in folder
        ClearFolder();

        FileVector int2 = new FileVector("int", 2);
        FileVector int3 = new FileVector("int", 3);
        FileVector int4 = new FileVector("int", 4);

        FileVector uint2 = new FileVector("uint", 2);
        FileVector uint3 = new FileVector("uint", 3);
        FileVector uint4 = new FileVector("uint", 4);
        
        WriteFile("int2.cs", int2.GenerateContent());
        WriteFile("int3.cs", int3.GenerateContent());
        WriteFile("int4.cs", int4.GenerateContent());

        WriteFile("uint2.cs", uint2.GenerateContent());
        WriteFile("uint3.cs", uint3.GenerateContent());
        WriteFile("uint4.cs", uint4.GenerateContent());
    }
}