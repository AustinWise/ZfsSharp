using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ZfsSharpLib;

class Sha256 : IChecksum
{
    public zio_cksum_t Calculate(ReadOnlySpan<byte> input)
    {
        zio_cksum_t ret;
        Unsafe.SkipInit(out ret);
        int bytesWritten = SHA256.HashData(input, MemoryMarshal.AsBytes(new Span<zio_cksum_t>(ref ret)));
        Debug.Assert(bytesWritten == Unsafe.SizeOf<zio_cksum_t>());
        ret.word1 = BinaryPrimitives.ReverseEndianness(ret.word1);
        ret.word2 = BinaryPrimitives.ReverseEndianness(ret.word2);
        ret.word3 = BinaryPrimitives.ReverseEndianness(ret.word3);
        ret.word4 = BinaryPrimitives.ReverseEndianness(ret.word4);
        return ret;
    }
}
