using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Vocore.Test
{
    public class Test_FileReference
    {
        [Test("test load")]
        public void Test_Load(){
            string path = "TestFiles/TestIO_1.xml";
            FileReference file = new FileReference(path);
            //TestUtility.PrintBlue(file.GetString());
            TestUtility.Assert(file.GetString() == null || file.GetString().Length == 0);
        }

        [Test("test load async")]
        public void Test_LoadAsync(){
            string path = "TestFiles/TestIO_1.xml";
            FileReference file = new FileReference(path);
            var task = file.LoadStringAsync((str) => {
                //TestUtility.PrintBlue(str);
                TestUtility.Assert(str == null || str.Length == 0);
            });
            TestUtility.PrintBlue("start load");
            Task.WaitAll(task);
        }

        [Test("test load xml async")]
        public void Test_LoadXmlAsync(){
            string path = "TestFiles/TestIO_1.xml";
            XmlReference file = new XmlReference(path);
            var task = file.LoadXmlAsync((doc) => {
                //TestUtility.PrintBlue(doc.ToString());
                TestUtility.Assert(doc == null || doc.ToString().Length == 0);
            });
            TestUtility.PrintBlue("start load");
            Task.WaitAll(task);
        }

        [Test("test IO : file not found", true)]
        public void Test_FileNotFound(){
            string path = "TestFiles/FileNotExist.xml";
            FileReference file = new FileReference(path);
            //throw exception
            file.GetBytes();
        }

        [Test("test IO performance: sync vs async")]
        public void Test_Performance(){
            int count = 1000;
            string path = "TestFiles/TestIO_1.xml";
            XmlReference file = new XmlReference(path);
            
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for(int i = 0; i < count; i++){
                file.LoadXml();
            }
            sw.Stop();
            TestUtility.PrintBlue("sync: " + sw.ElapsedMilliseconds);
            
            Task[] tasks = new Task[count];
            sw.Restart();
            for(int i = 0; i < count; i++){
                tasks[i] = file.LoadXmlAsync((doc) => {});
            }
            Task.WaitAll(tasks);
            sw.Stop();
            TestUtility.PrintBlue("async: " + sw.ElapsedMilliseconds);
        }
    }
}

