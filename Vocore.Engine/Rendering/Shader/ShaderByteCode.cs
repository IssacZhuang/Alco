using System;
using System.Collections.Generic;

using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public struct ShaderByteCode
    {
        private SpirvCompilationResult _spirv;
        public string filename;
        
        public byte[] Bytes => _spirv.SpirvBytes;
        public ShaderByteCode(SpirvCompilationResult spirv, string filename)
        {
            _spirv = spirv;
            this.filename = filename;
        }
    }
}

