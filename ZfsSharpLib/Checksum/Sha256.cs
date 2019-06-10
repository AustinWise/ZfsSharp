using System;
using System.Security.Cryptography;

namespace ZfsSharpLib
{
    class Sha256 : IChecksum
    {
        public zio_cksum_t Calculate(ArraySegment<byte> input)
        {
            byte[] checksumBytes;
            using (var sha = SHA256.Create())
            {
                checksumBytes = sha.ComputeHash(input.Array, input.Offset, input.Count);
            }
            return Program.ToStructByteSwap<zio_cksum_t>(checksumBytes);
        }
    }
}
