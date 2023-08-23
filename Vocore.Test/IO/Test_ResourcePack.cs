using System;
using System.IO;
using System.Collections.Generic;

namespace Vocore.Test
{
    public class Test_ResourcePack
    {
        public static string GetPackPath()
        {
            string filename = "test.zip";
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
        }

        [Test("Test ResourcePack write and read bianry")]
        public void Test_Binary()
        {

            string path = GetPackPath();
            byte[] data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            using (ResourcePack pack = new ResourcePack(path))
            {
                pack.TrySetFile("test.bin", data);
            }

            using (ResourcePack pack = new ResourcePack(path))
            {
                if (pack.TryGetFileBinary("test.bin", out byte[] result))
                {
                    TestHelper.AssertFalse(result == null);
                    TestHelper.AssertFalse(result.Length == 0);
                    bool isCorrect = true;
                    for (int i = 0; i < data.Length; i++)
                    {
                        isCorrect = isCorrect && (data[i] == result[i]);
                    }
                    TestHelper.AssertTrue(isCorrect);
                }
                else
                {
                    TestHelper.AddFailed();
                }
            }
        }

        [Test("Test ResourcePack write and read text")]
        public void Test_Text()
        {
            string path = GetPackPath();
            string text = "Hello World!";
            using (ResourcePack pack = new ResourcePack(path))
            {
                pack.TrySetTextFile("test.txt", text);
            }

            using (ResourcePack pack = new ResourcePack(path))
            {
                if (pack.TryGetFileText("test.txt", out string result))
                {
                    TestHelper.AssertFalse(result == null);
                    TestHelper.AssertFalse(result.Length == 0);
                    TestHelper.PrintBlue(result);
                    TestHelper.AssertTrue(text == result);
                }
                else
                {
                    TestHelper.AddFailed();
                }
            }
        }
    }
}

