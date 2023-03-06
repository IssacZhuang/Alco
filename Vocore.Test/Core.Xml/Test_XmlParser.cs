using System;
using System.Xml;
using System.Reflection;
using Vocore.Xml;
using System.IO;

using UnityEngine;


namespace Vocore.Test
{
    public enum TestEnum
    {
        Primary,
        Secondary,
        Tertiary
    }

    // refer to xml
    // <TestClass>
    //     <Name>TestObject</Name>
    //     <Position>(1,2,3)</Position>
    //     <Rotation>(0.8,0.8,0.8,0.8)</Rotation>
    //     <Scale>(1,1,1)</Scale>
    //     <TestInt>1</TestInt>
    //     <TestFloat>1.1</TestFloat>
    //     <TestString>TestString\\n aaa</TestString>
    //     <TestBool>True</TestBool>
    //     <TestEnum>Secondary</TestEnum>
    // </TestClass>
    public class TestClass
    {
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public int TestInt;
        public float TestFloat;
        public string TestString;
        public bool TestBool;
        public TestEnum TestEnum;
        public InnerDataClass InnerData;
    }

    public class TestClassChild : TestClass
    {
        public string ChildName;
    }

    public class InnerDataClass
    {
        public string Key;
        public int Value;

        public override string ToString()
        {
            return string.Format("Key: {0}, Value: {1}", Key, Value);
        }
    }

    public class Test_XmlParser
    {
        [Test("Test_LoadXmlFile")]
        public void Test_LoadXmlFile()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            //iterate all file in current assembly
            foreach (var fileName in currentAssembly.GetManifestResourceNames())
            {
                TestUtility.PrintGray(fileName + " loaded");
            }
        }

        [Test("Test_ParseXml")]
        public void Test_ParseXml()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            string path = "Vocore.Test.Core.Xml.TestFiles.TestObject.xml";

            // get resource stream from xml file location inside the assembly
            using (Stream stream = currentAssembly.GetManifestResourceStream(path))
            {
                // read xml contents from stream and format it 
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);
                    string formattedXml = xmlDoc.InnerXml;

                    string xPath = "Objects/Vocore.Test.TestClass[Name = 'TestObject']";
                    XmlNode xmlNode = xmlDoc.SelectSingleNode(xPath);

                    TestUtility.PrintBlue("Type of xmlNode: " + Type.GetType("Vocore.Test.TestClass"));

                    // parse xml content
                    TestClass testClass = xmlNode.ParseToObject() as TestClass;

                    foreach (string error in XmlParser.GetErrors())
                    {
                        TestUtility.PrintRed(error);
                    }
                    XmlParser.ClearErrors();

                    if (testClass == null)
                    {
                        TestUtility.AddFailed();
                        return;
                    }



                    TestUtility.PrintBlue(TestUtility.DumpToString(testClass));
                }
            }
        }


        [Test("Test_ParseXml - Missing Content")]
        public void Test_ParseXml_MissingContent()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            string path = "Vocore.Test.Core.Xml.TestFiles.TestObject.xml";

            // get resource stream from xml file location inside the assembly
            using (Stream stream = currentAssembly.GetManifestResourceStream(path))
            {
                // read xml contents from stream and format it 
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);
                    string formattedXml = xmlDoc.InnerXml;

                    string xPath = "Objects/Vocore.Test.TestClass[Name = 'MissingContent']";
                    XmlNode xmlNode = xmlDoc.SelectSingleNode(xPath);

                    TestUtility.PrintBlue("Type of xmlNode: " + Type.GetType("Vocore.Test.TestClass"));

                    // parse xml content
                    TestClass testClass = xmlNode.ParseToObject() as TestClass;

                    foreach (string error in XmlParser.GetErrors())
                    {
                        TestUtility.PrintRed(error);
                    }
                    XmlParser.ClearErrors();

                    if (testClass == null)
                    {
                        TestUtility.AddFailed();
                        return;
                    }

                    TestUtility.PrintBlue(TestUtility.DumpToString(testClass));
                }
            }
        }


        [Test("Test_ParseXml - Child")]
        public void Test_ParseXml_Child()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            string path = "Vocore.Test.Core.Xml.TestFiles.TestObject.xml";

            // get resource stream from xml file location inside the assembly
            using (Stream stream = currentAssembly.GetManifestResourceStream(path))
            {
                // read xml contents from stream and format it 
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);
                    string formattedXml = xmlDoc.InnerXml;

                    string xPath = "Objects/Vocore.Test.TestClass[Name = 'TestObject2']";
                    XmlNode xmlNode = xmlDoc.SelectSingleNode(xPath);

                    TestUtility.PrintBlue("Type of xmlNode: " + Type.GetType("Vocore.Test.TestClassChild"));

                    // parse xml content
                    TestClassChild testClass = xmlNode.ParseToObject() as TestClassChild;

                    foreach (string error in XmlParser.GetErrors())
                    {
                        TestUtility.PrintRed(error);
                    }
                    XmlParser.ClearErrors();

                    if (testClass == null)
                    {
                        TestUtility.AddFailed();
                        return;
                    }

                    TestUtility.PrintBlue(TestUtility.DumpToString(testClass));
                }
            }
        }
    }
}


