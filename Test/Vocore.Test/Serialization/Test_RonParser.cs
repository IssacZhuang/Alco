using System.IO;
using Ron.NET;

namespace Vocore.Test;

public class Test_RonParser
{
    [Test]
    public void Test_Parse()
    {
        string ronFile = File.OpenText("TestFiles/TestRon.ron").ReadToEnd();
        IElement root = RON.Parse(ronFile);
        float valueFloat = (float)(root["valueFloat"] as ValueElement<double>).Value;
        Assert.That(valueFloat, Is.EqualTo(3.14f));

        string valueString = (root["valueString"] as ValueElement<string>).Value;
        Assert.That(valueString, Is.EqualTo("TestString"));

        bool valueBool = (root["valueBool"] as ValueElement<bool>).Value;
        Assert.That(valueBool, Is.True);

        int valueInt = (int)(root["valueInt"] as ValueElement<double>).Value;
        Assert.That(valueInt, Is.EqualTo(42));
    }
}