namespace Vocore.Rendering;

public struct ShaderPragma
{
    public string Name { get; init; }
    public string[] Values { get; init; }

    public byte[] EncodeToBinary()
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < Values.Length; i++)
        {
            array.Add(Values[i]);
        }

        BinaryTable table = new BinaryTable
        {
            {"Name", Name },
            {"Values", array },
        };

        return BinaryParser.EncodeTable(table);
    }

    public static ShaderPragma DecodeFromBinary(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetString("Name", out string? name) && table.TryGetArray("Values", out BinaryArray? values))
        {
            string[] valuesArray = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                if (values.TryGetString(i, out string? value))
                {
                    valuesArray[i] = value;
                }
            }

            ShaderPragma pragma = new ShaderPragma
            {
                Name = name,
                Values = valuesArray,
            };

            return pragma;
        }

        throw new Exception("Unable to decode ShaderPragma from binary data.");
    }

    public override string ToString()
    {
        return $"#pragma {Name} {string.Join(" ", Values)}";
    }
}