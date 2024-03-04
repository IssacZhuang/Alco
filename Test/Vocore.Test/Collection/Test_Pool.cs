namespace Vocore.Test
{
    public class Test_Pool
    {
        [Test(Description = "ArrayPool")]
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
                // object item = pool.Get();
                // if (item != array[i])
                // {
                //     //UnitTest.AddFailed();
                //     Assert.Fail("ArrayPool get failed");
                //     return;
                // }
                if(pool.TryGet(out object item))
                {
                    if (item != array[i])
                    {
                        Assert.Fail("ArrayPool get failed");
                    }
                }
                else
                {
                    Assert.Fail("ArrayPool get failed");
                }
            }

            //Assert.IsFalse(pool.Get() != null, "ArrayPool get failed");
            if(pool.TryGet(out object item2))
            {
                Assert.Fail("ArrayPool get failed");
            }

        }
    }
}


