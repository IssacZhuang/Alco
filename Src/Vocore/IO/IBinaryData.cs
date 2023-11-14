using System;

namespace Vocore
{
    interface IBinaryData
    {
        byte[] GetBinaryData();
        void SetBinaryData(byte[] data);
        int ByteSize { get; }
    }
}