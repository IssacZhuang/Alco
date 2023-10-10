using System;
using System.Collections.Generic;

using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public struct ShaderByteCode
    {
        private SpirvCompilationResult _spirv;
        private string _filename;
        public SpirvCompilationResult Spirv => _spirv;
        public byte[] Bytes => _spirv.SpirvBytes;
        public string Filename => _filename;
        public ShaderByteCode(SpirvCompilationResult spirv, string filename)
        {
            _spirv = spirv;
            _filename = filename;
        }
    }
}

