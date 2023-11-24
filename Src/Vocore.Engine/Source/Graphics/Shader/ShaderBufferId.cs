using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public struct ShaderBufferId
    {
        private uint _id;
        public uint Id
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _id;
        }

        public int Set
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)(_id >> 16);
        }

        public int Binding
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)(_id & 0xFFFF);
        }

        public ShaderBufferId(int set, int binding)
        {
            _id = (uint)(set << 16 | binding);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is ShaderBufferId id && _id == id._id;
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(_id);
        }
    }
}