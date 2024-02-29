using System;
using System.Collections.Generic;
using System.Xml;
using System.Reflection;
using Vocore;
using System.IO;

using System.Numerics;


namespace Vocore.Test
{
    public enum TestEnum : int
    {
        Primary = 1,
        Secondary = 2,
        Tertiary = 4
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
    public class TestCls
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
        public TestStruct TestStruct;
        public TestCls RecursiveClass;
        public List<KeyValuePair<Type, int>> TestDictionary;
    }

    public class TestClassChild : TestCls
    {
        public string ChildName;
        public List<int> IntList;
        public List<InnerDataClass> DataList;
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

    public struct TestStruct
    {
        public string Name;
        public int Value;

        public override string ToString()
        {
            return string.Format("Name: {0}, Value: {1}", Name, Value);
        }
    }

    public class Test_XmlParser
    {
        [Test(Description = "Test_LoadXmlFile")]
        public void Test_LoadXmlFile()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            TestEnum e = TestEnum.Primary | TestEnum.Secondary;

            TestContext.WriteLine("TestEnum: " + (e | TestEnum.Tertiary));

            //iterate all file in current assembly
            foreach (var fileName in currentAssembly.GetManifestResourceNames())
            {
                TestContext.WriteLine(fileName + " loaded");
            }
        }

        [Test(Description = "Test_ParseXml")]
        public void Test_ParseXml()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            string path = "Vocore.Test.TestFiles.TestObject.xml";

            XmlParser parser = new XmlParser("Vocore.Test");

            // get resource stream from xml file location inside the assembly
            using (Stream stream = currentAssembly.GetManifestResourceStream(path))
            {
                // read xml contents from stream and format it 
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);
                    string formattedXml = xmlDoc.InnerXml;

                    string xPath = "Objects/TestCls[Name = 'TestObject']";
                    XmlNode xmlNode = xmlDoc.SelectSingleNode(xPath);

                    // parse xml content
                    TestCls testClass = parser.ParseToObject(xmlNode) as TestCls;

                    Assert.IsFalse(testClass == null, "ParseXml failed");

                    //TestHelper.PrintBlue(UtilsLog.DumpToString(testClass)+"\n\n");
                }
            }
        }


        [Test(Description = "ParseXml - Missing Content")]
        public void Test_ParseXml_MissingContent()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            string path = "Vocore.Test.TestFiles.TestObject.xml";

            XmlParser parser = new XmlParser("Vocore.Test");

            // get resource stream from xml file location inside the assembly
            using (Stream stream = currentAssembly.GetManifestResourceStream(path))
            {
                // read xml contents from stream and format it 
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);
                    string formattedXml = xmlDoc.InnerXml;

                    string xPath = "Objects/TestCls[Name = 'MissingContent']";
                    XmlNode xmlNode = xmlDoc.SelectSingleNode(xPath);



                    // parse xml content
                    TestCls testClass = parser.ParseToObject(xmlNode) as TestCls;

                    Assert.IsFalse(testClass == null, "ParseXml MissingContent failed");

                    //TestHelper.PrintBlue(UtilsLog.DumpToString(testClass)+"\n\n");
                }
            }
        }


        [Test(Description = "Test_ParseXml - Child")]
        public void Test_ParseXml_Child()
        {
            //load xml file from current assembly
            Assembly currentAssembly = Assembly.GetExecutingAssembly();

            string path = "Vocore.Test.TestFiles.TestObject.xml";

            XmlParser parser = new XmlParser("Vocore.Test");

            // get resource stream from xml file location inside the assembly
            using (Stream stream = currentAssembly.GetManifestResourceStream(path))
            {
                // read xml contents from stream and format it 
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(reader);
                    string formattedXml = xmlDoc.InnerXml;

                    string xPath = "Objects/TestCls[Name = 'TestObject2']";
                    XmlNode xmlNode = xmlDoc.SelectSingleNode(xPath);

                    // parse xml content
                    TestClassChild testClass = parser.ParseToObject(xmlNode) as TestClassChild;

                    Assert.IsFalse(testClass == null, "ParseXml Child failed");

                    //TestHelper.PrintBlue(UtilsLog.DumpToString(testClass)+"\n\n");
                }
            }
        }
    }
}


