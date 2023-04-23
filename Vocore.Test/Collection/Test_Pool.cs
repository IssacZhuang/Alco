namespace Vocore.Test
{
    public class Test_Pool
    {
        [Test("Test_ArrayPool")]
        public void Test_ArrayPool()
        {
            ArrayPool<object> pool = new ArrayPool<object>(10);

            object[] array = new object[10];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new object();
            }

            for (int i = 0; i < array.Length; i++)
            {
                pool.Return(array[i]);
            }

            pool.Return(new object());

            for (int i = array.Length - 1; i >= 0; i--)
            {
                object item = pool.Get();
                if (item != array[i])
                {
                    TestHelper.AddFailed();
                    TestHelper.PrintRed("Test_ArrayPool return failed");
                    return;
                }
            }

            TestHelper.Assert(pool.Get() != null, "Test_ArrayPool get failed");

        }
    }
}


