

public class GeneratorMath : BaseGenerator
{
    public override string OutputFolder => "Src/Vocore/Math";

    public override void Generate()
    {
        FileVector int2 = new FileVector("int", 2);
        FileVector int3 = new FileVector("int", 3);
        FileVector int4 = new FileVector("int", 4);

        FileVector uint2 = new FileVector("uint", 2);
        FileVector uint3 = new FileVector("uint", 3);
        FileVector uint4 = new FileVector("uint", 4);
        
    }
}