﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ZfsSharp.HardDisks;
using System.Reflection;
using System.Runtime.CompilerServices;

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
    }

    class Program
    {
        const ulong UbMagic = 0x00bab10c;


        unsafe static void Main(string[] args)
        {
            var file = new FileHardDisk(@"D:\VPC\SmartOs\SmartOs.vhd");
            var vhd = VhdHardDisk.Create(file);
            var gpt = new GptHardDrive(vhd);

            List<uberblock_t> blocks = new List<uberblock_t>();
            for (long i = 0; i < 128; i++)
            {
                var offset = (128 << 10) + 1024 * i;
                uberblock_t b;
                gpt.Get<uberblock_t>(offset, out b);
                if (b.Magic == UbMagic)
                    blocks.Add(b);
            }
            var ub = blocks.OrderByDescending(u => u.Txg).First();

            NvList nv;
            using (var s = new MemoryStream(gpt.ReadBytes(16 << 10, 112 << 10)))
                nv = new NvList(s);
            if (nv.Get<ulong>("version") != 5000)
            {
                throw new NotSupportedException();
            }
            var diskGuid = nv.Get<UInt64>("guid");

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            var dev = new OffsetHardDisk(gpt, VDevLableSizeStart, gpt.Length - VDevLableSizeStart - VDevLableSizeEnd);
            var zio = new Zio(new[] { dev });
            var dmu = new Dmu(zio);
            var zap = new Zap(dmu);
            var dsl = new Dsl(ub.rootbp, zap, dmu, zio);

            var rootZpl = dsl.GetRootDataSet();
            //var fileContents = Encoding.ASCII.GetString(rootZpl.GetFileContents("/currbooted"));
            //var fileContents2 = Encoding.ASCII.GetString(rootZpl.GetFileContents("/global/asdf"));

            var root = rootZpl.Root;
            var children = root.GetChildren().ToArray();

            Console.WriteLine();

            foreach (var ds in dsl.ListDataSet())
            {
                //TODO: a better way of detecting the type of dataset
                if (ds.Key.Contains("$") || ds.Key.Contains("/dump") || ds.Key.Contains("/swap"))
                    continue;
                var zpl = dsl.GetDataset(ds.Value);
                printContent(ds.Key, zpl.Root);
            }

            /*
             * TODO:
             *  DSL
             *  Complete Fat ZAP
             *  ZPL
             */

            Console.WriteLine();
        }

        static void printContent(string namePrefix, Zpl.ZfsItem item)
        {
            Console.WriteLine(namePrefix + item.FullPath);
            var dir = item as Zpl.ZfsDirectory;
            if (dir == null)
                return;
            foreach (var d in dir.GetChildren())
            {
                printContent(namePrefix, d);
            }
        }

        public static T ToStruct<T>(byte[] bytes) where T : struct
        {
            return ToStruct<T>(bytes, 0);
        }

        public unsafe static T ToStruct<T>(byte[] bytes, long offset) where T : struct
        {
            fixed (byte* ptr = bytes)
                return ToStruct<T>(ptr, offset, bytes.Length);
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
            { typeof(Guid), 16 }, //TODO: determine if a GUID should really be byte swapped this way
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

        public static void GetMultiBlockCopyOffsets(int blockNdx, int totalBlocks, long blockSize, long dataOffset, long dataSize, out long startNdx, out long cpyCount)
        {
            startNdx = 0;
            cpyCount = blockSize;
            if (blockNdx == 0)
                startNdx += (int)(dataOffset % blockSize);
            if (blockNdx == totalBlocks - 1)
            {
                cpyCount = (int)((dataOffset + dataSize) % blockSize);
                if (cpyCount == 0)
                    cpyCount = (int)blockSize;
                cpyCount -= startNdx;
            }
        }

        /// <summary>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockKey">An identifier for the block.</param>
        /// <param name="dest">The place to store the read data.</param>
        /// <param name="destOffset">The offset within dest to place the data.</param>
        /// <param name="startNdx">The offset within the block to start reading from.</param>
        /// <param name="cpyCount">The number of bytes to read.</param>
        public delegate void BlockReader<T>(T blockKey, byte[] dest, long destOffset, long startNdx, long cpyCount);

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
        public static void MultiBlockCopy<T>(byte[] dest, long destOffset, long offset, long size, long blockSize, Func<long, T> GetBlockKey, BlockReader<T> ReadBlock)
        {
            List<T> dataBlockPtrs = new List<T>();
            for (long i = offset; i < (offset + size); i += blockSize)
            {
                long blockId = i / blockSize;
                dataBlockPtrs.Add(GetBlockKey(blockId));
            }

            long retNdx = destOffset;
            for (int i = 0; i < dataBlockPtrs.Count; i++)
            {
                long startNdx, cpyCount;
                Program.GetMultiBlockCopyOffsets(i, dataBlockPtrs.Count, blockSize, offset, size, out startNdx, out cpyCount);

                ReadBlock(dataBlockPtrs[i], dest, retNdx, startNdx, cpyCount);
                retNdx += blockSize;
            }
        }

        public static void LongBlockCopy(byte[] src, long srcOffset, byte[] dst, long dstOffset, long count)
        {
            if (src == null || dst == null)
                throw new ArgumentNullException();
            if (srcOffset < 0 || dstOffset < 0 || count <= 0)
                throw new ArgumentOutOfRangeException();
            if (srcOffset + count > src.LongLength)
                throw new ArgumentOutOfRangeException();
            if (dstOffset + count > dst.LongLength)
                throw new ArgumentOutOfRangeException();
            for (long i = 0; i < count; i++)
            {
                dst[i + dstOffset] = src[i + srcOffset];
            }
        }

        public const int SPA_MINBLOCKSHIFT = 9;
        public const int SPA_MAXBLOCKSHIFT = 17;
        const long SPA_MINBLOCKSIZE = (1L << SPA_MINBLOCKSHIFT);
        const long SPA_MAXBLOCKSIZE = (1L << SPA_MAXBLOCKSHIFT);

        const string ROOT_DATASET = "root_dataset";
        const string CONFIG = "config";
    }


}
