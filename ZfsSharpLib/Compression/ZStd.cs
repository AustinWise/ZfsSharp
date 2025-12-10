
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using ZstdSharp.Unsafe;
using ZstdSharp;

namespace ZfsSharpLib;

class ZStd : ICompression
{
    private const ZSTD_dParameter ZSTD_d_format = ZSTD_dParameter.ZSTD_d_experimentalParam1;
    private const int ZSTD_f_zstd1_magicless = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct zfs_zstd_header
    {
        public uint c_len;
        public uint raw_version_level;
    }

    public unsafe void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var header = MemoryMarshal.Read<zfs_zstd_header>(input);
        uint compressedLength = header.c_len;
        if (BitConverter.IsLittleEndian)
            compressedLength = BinaryPrimitives.ReverseEndianness(compressedLength);
        input = input.Slice(sizeof(zfs_zstd_header), checked((int)compressedLength));
        using var decompresser = new ZstdSharp.Decompressor();
        decompresser.SetParameter(ZSTD_d_format, ZSTD_f_zstd1_magicless);
        if (!decompresser.TryUnwrap(input, output, out int written))
        {
            throw new Exception("Failed to decompress");
        }
    }
}