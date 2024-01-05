using System;
using Silk.NET.SPIRV.Cross;

namespace Vocore.Engine
{
    public static class UtilsSpirvCross
    {
        private static readonly Cross _cross = Cross.GetApi();
        public static Cross Cross => _cross;

        
    }
}