using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ZfsSharpLib
{
    class NvListBinaryReader
    {
        readonly Stream mStream;
        readonly Encoding mEnc;
        public NvListBinaryReader(Stream s)
            : this(s, Encoding.UTF8)
        {
        }
        public NvListBinaryReader(Stream s, Encoding enc)
        {
            mStream = s;
            mEnc = enc;
        }

        private ReadOnlySpan<byte> ReadInternal(Span<byte> buffer)
        {
            mStream.ReadExactly(buffer);
            return buffer;
        }

        public byte ReadByte()
        {
            int ret = mStream.ReadByte();
            if (ret == -1)
                throw new EndOfStreamException();
            return (byte)ret;
        }

        public short ReadInt16()
        {
            return BinaryPrimitives.ReadInt16BigEndian(ReadInternal(stackalloc byte[sizeof(short)]));
        }

        public int ReadInt32()
        {
            return BinaryPrimitives.ReadInt32BigEndian(ReadInternal(stackalloc byte[sizeof(int)]));
        }

        public long ReadInt64()
        {
            return BinaryPrimitives.ReadInt64BigEndian(ReadInternal(stackalloc byte[sizeof(long)]));
        }

        public ushort ReadUInt16()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(ReadInternal(stackalloc byte[sizeof(ushort)]));
        }

        public uint ReadUInt32()
        {
            return BinaryPrimitives.ReadUInt32BigEndian(ReadInternal(stackalloc byte[sizeof(uint)]));
        }

        public ulong ReadUInt64()
        {
            return BinaryPrimitives.ReadUInt64BigEndian(ReadInternal(stackalloc byte[sizeof(ulong)]));
        }

        public string ReadString()
        {
            int size = ReadInt32();
            Span<byte> buffer = stackalloc byte[checked(size + 3) & ~3];
            mStream.ReadExactly(buffer);
            return mEnc.GetString(buffer.Slice(0, size));
        }
    }
}
