using System;
using System.IO;
using System.Reflection;
using System.Text;
using Vocore;
using Vocore.Unsafe;

namespace Vocore.Test
{
    internal class Entry
    {
        static void Main(string[] _)
        {
            try
            {
                Assembly assembly = Assembly.GetAssembly(typeof(Entry));
                TestHelper.StartTest(assembly);
            }
            catch (ReflectionTypeLoadException ex)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Exception exSub in ex.LoaderExceptions)
                {
                    sb.AppendLine(exSub.Message);
                    FileNotFoundException exFileNotFound = exSub as FileNotFoundException;
                    if (exFileNotFound != null)
                    {
                        if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                        {
                            sb.AppendLine("Fusion Log:");
                            sb.AppendLine(exFileNotFound.FusionLog);
                        }
                    }
                    sb.AppendLine();
                }
                string errorMessage = sb.ToString();
                TestHelper.PrintRed(errorMessage);
            }
            //PointerTracker.Instance.DisplayResult();
            foreach (var item in PointerTracker.GetAllocatedStackTrace())
            {
                TestHelper.PrintRed(item);
            }

            Console.ReadLine();

        }
    }
}
