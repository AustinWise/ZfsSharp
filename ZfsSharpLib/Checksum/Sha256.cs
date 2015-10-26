using System;
using System.Security.Cryptography;

namespace ZfsSharp
{
    class Sha256 : IChecksum
    {
        public zio_cksum_t Calculate(ArraySegment<byte> input)
        {
            var sha = SHA256.Create();
            var checksumBytes = sha.ComputeHash(input.Array, input.Offset, input.Count);
            return Program.ToStructByteSwap<zio_cksum_t>(checksumBytes);
        }
    }
}
