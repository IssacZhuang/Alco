using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco;

public class AccessorTypeInfo
{
    private static MemberAccessor? s_memberAccessor;
    private static MemberAccessor MemberAccessor
    {
        get
        {
            return s_memberAccessor ?? Initialize();
            static MemberAccessor Initialize()
            {
                MemberAccessor value =

                    // if dynamic code isn't supported, fallback to reflection
                    RuntimeFeature.IsDynamicCodeSupported ?
                        new ReflectionEmitCachingMemberAccessor() :
                        new ReflectionMemberAccessor();
                return Interlocked.CompareExchange(ref s_memberAccessor, value, null) ?? value;
            }
        }
    }
}

