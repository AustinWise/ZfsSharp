﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct uberblock_t
    {
        public ulong Magic;
        public ulong Version;
        public ulong Txg;
        public ulong GuidSum;
        public ulong TimeStamp;
        public blkptr_t rootbp;

        public const ulong UbMagic = 0x00bab10c;
    }

    static class Program
    {
        public static T ToStruct<T>(byte[] bytes) where T : struct
        {
            return ToStruct<T>(bytes, 0);
        }

        public unsafe static T ToStruct<T>(byte[] bytes, long offset) where T : struct
        {
            fixed (byte* ptr = bytes)
                return ToStruct<T>(ptr, offset, bytes.Length);
        }

        public static T ToStruct<T>(ArraySegment<byte> bytes) where T : struct
        {
            if (Marshal.SizeOf(typeof(T)) != bytes.Count)
                throw new ArgumentOutOfRangeException();
            return ToStruct<T>(bytes.Array, bytes.Offset);
        }

        public unsafe static T ToStruct<T>(byte* ptr, long offset, long ptrLength) where T : struct
        {
            if (offset < 0 || ptrLength <= 0)
                throw new ArgumentOutOfRangeException();
            Type t = typeof(T);
            if (offset + Marshal.SizeOf(t) > ptrLength)
                throw new ArgumentOutOfRangeException();
            return (T)Marshal.PtrToStructure(new IntPtr(ptr + offset), t);
        }

        public static T ToStructByteSwap<T>(byte[] bytes) where T : struct
        {
            var copy = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, copy, 0, copy.Length);
            foreach (var f in typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                ByteSwapField<T>(f.Name, f.FieldType, copy);
            }
            return ToStruct<T>(copy);
        }

        static Dictionary<Type, int> sStructSize = new Dictionary<Type, int>()
        {
            { typeof(byte), 1 },
            { typeof(sbyte), 1 },
            { typeof(short), 2 },
            { typeof(ushort), 2 },
            { typeof(int), 4 },
            { typeof(uint), 4 },
            { typeof(long), 8 },
            { typeof(ulong), 8 },
            { typeof(Guid), 16 },
        };

        static void ByteSwapField<T>(string fieldName, Type fieldType, byte[] byteArray) where T : struct
        {
            var itemOffset = Marshal.OffsetOf(typeof(T), fieldName).ToInt32();
            ByteSwap(fieldType, byteArray, itemOffset);
        }

        public static void ByteSwap(Type type, byte[] byteArray, int itemOffset)
        {
            int itemSize;
            if (!sStructSize.TryGetValue(type, out itemSize))
            {
                if (type.IsEnum)
                {
                    var realType = type.GetEnumUnderlyingType();
                    ByteSwap(realType, byteArray, itemOffset);
                    return;
                }
                else if (type.GetCustomAttributes(typeof(UnsafeValueTypeAttribute), false).Length != 0)
                {
                    return;
                    //ignore fixed size buffers
                }
                else
                    throw new NotSupportedException();
            }

            for (int byteNdx = 0; byteNdx < itemSize / 2; byteNdx++)
            {
                int lowerNdx = itemOffset + byteNdx;
                int higherNdx = itemOffset + itemSize - byteNdx - 1;
                byte b = byteArray[lowerNdx];
                byteArray[lowerNdx] = byteArray[higherNdx];
                byteArray[higherNdx] = b;
            }
        }

        public static void GetMultiBlockCopyOffsets(int blockNdx, int totalBlocks, int blockSize, long dataOffset, int dataSize, out int startNdx, out int cpyCount)
        {
            startNdx = 0;
            long tmpCpyCount = blockSize;
            if (blockNdx == 0)
            {
                startNdx += (int)(dataOffset % blockSize);
                tmpCpyCount -= startNdx;
            }
            if (blockNdx == totalBlocks - 1)
            {
                tmpCpyCount = (dataOffset + dataSize);
                if ((tmpCpyCount % blockSize) == 0)
                    tmpCpyCount = blockSize;
                tmpCpyCount -= startNdx;
            }

            tmpCpyCount = tmpCpyCount % blockSize;
            if (tmpCpyCount == 0)
                tmpCpyCount = blockSize;

            cpyCount = (int)tmpCpyCount;
        }

        /// <summary>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockKey">An identifier for the block.</param>
        /// <param name="dest">The place to store the read data.</param>
        /// <param name="destOffset">The offset within dest to place the data.</param>
        /// <param name="startNdx">The offset within the block to start reading from.</param>
        /// <param name="cpyCount">The number of bytes to read.</param>
        public delegate void BlockReader<T>(T blockKey, byte[] dest, int destOffset, int startNdx, int cpyCount);

        /// <summary>
        /// Given a large amount of data stored in equal sized blocks, reads a subset of that data efficiently.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dest">The place to store the read data.</param>
        /// <param name="destOffset">The offset in dest where to store the data.</param>
        /// <param name="offset">The byte offset into the blocks to read.</param>
        /// <param name="size">The number of bytes to read.</param>
        /// <param name="blockSize">The size of the blocks.</param>
        /// <param name="GetBlockKey">Given a block offset returns a key for reading that block.</param>
        /// <param name="ReadBlock">Given a block key, reads the block.</param>
        public static void MultiBlockCopy<T>(byte[] dest, int destOffset, long offset, int size, int blockSize, Func<long, T> GetBlockKey, BlockReader<T> ReadBlock)
        {
            if (dest == null)
                throw new ArgumentNullException("dest");
            if (destOffset < 0)
                throw new ArgumentOutOfRangeException("destOffset");
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            if (size < 0)
                throw new ArgumentOutOfRangeException("size");
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException("blockSize");

            List<T> blockKeys = new List<T>();
            for (long i = (offset / blockSize) * blockSize; i < (offset + size); i += blockSize)
            {
                long blockId = i / blockSize;
                blockKeys.Add(GetBlockKey(blockId));
            }

            int retNdx = destOffset;
            for (int i = 0; i < blockKeys.Count; i++)
            {
                int startNdx, cpyCount;
                Program.GetMultiBlockCopyOffsets(i, blockKeys.Count, blockSize, offset, size, out startNdx, out cpyCount);

                ReadBlock(blockKeys[i], dest, retNdx, startNdx, cpyCount);
                retNdx += cpyCount;
            }
        }

        public static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        public const int SPA_MINBLOCKSHIFT = 9;
        public const int SPA_MAXBLOCKSHIFT = 17;
        const long SPA_MINBLOCKSIZE = (1L << SPA_MINBLOCKSHIFT);
        const long SPA_MAXBLOCKSIZE = (1L << SPA_MAXBLOCKSHIFT);
    }


}
