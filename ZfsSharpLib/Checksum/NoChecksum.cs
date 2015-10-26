using System;

namespace ZfsSharp
{
    class NoChecksum : IChecksum
    {
        public zio_cksum_t Calculate(ArraySegment<byte> input)
        {
            return new zio_cksum_t();
        }
    }
}
