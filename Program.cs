using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ZfsSharp.VirtualDevices;

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
        static void Main(string[] args)
        {
            //args = new string[] { @"C:\VPC\SmartOs\" };
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ZfsSharp.exe <a directory containing VHD, VDI, or ZFS files>");
                return;
            }

            var hdds = LeafVdevInfo.GetLeafVdevs(args[0]);
            var vdevs = Vdev.CreateVdevTree(hdds);

            Zio zio = new Zio(vdevs);
            var dmu = new Dmu(zio);
            var zap = new Zap(dmu);
            Dsl dsl = new Dsl(hdds[0].Uberblock.rootbp, zap, dmu, zio);

            var rootZpl = dsl.GetRootDataSet();

            var root = rootZpl.Root;
            var children = root.GetChildren().ToArray();

            foreach (var ds in dsl.ListDataSet())
            {
                Console.WriteLine("{0}: {1}", ds.Key, ds.Value);
            }

            foreach (var ds in dsl.ListDataSet())
            {
                //TODO: a better way of detecting the type of dataset
                if (ds.Key.Contains("$MOS") || ds.Key.Contains("$FREE") || ds.Key.Contains("$ORIGIN") || ds.Key.Contains("/dump") || ds.Key.Contains("/swap"))
                    continue;

                var zpl = dsl.GetDataset(ds.Value);
                printContent(ds.Key, zpl.Root);

                if (ds.Key == "zones/var")
                    Console.WriteLine(Encoding.ASCII.GetString(zpl.GetFileContents(@"/svc/log/svc.startd.log")));
            }

            /*
             * TODO:
             *  DSL
             *  Complete Fat ZAP
             */

            Console.WriteLine();
        }

        private static void BenchmarkFileReading(Dsl dsl)
        {

            var varzpl = dsl.ListDataSet().Where(k => k.Key == "zones/var").Select(k => dsl.GetDataset(k.Value)).Single();
            Stopwatch st = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                varzpl.GetFileContents(@"/svc/log/svc.startd.log");
            }
            st.Stop();

            Console.WriteLine(st.Elapsed.TotalSeconds);
        }

        static void printContent(string namePrefix, Zpl.ZfsItem item)
        {
            Console.WriteLine(namePrefix + item.FullPath);
            var dir = item as Zpl.ZfsDirectory;
            var file = item as Zpl.ZfsFile;

            if (file != null)
            {
                file.GetContents();
            }

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

        public static void GetMultiBlockCopyOffsets(int blockNdx, int totalBlocks, long blockSize, long dataOffset, long dataSize, out long startNdx, out long cpyCount)
        {
            startNdx = 0;
            cpyCount = blockSize;
            if (blockNdx == 0)
            {
                startNdx += (dataOffset % blockSize);
                cpyCount -= startNdx;
            }
            if (blockNdx == totalBlocks - 1)
            {
                cpyCount = (dataOffset + dataSize);
                if ((cpyCount % blockSize) == 0)
                    cpyCount = blockSize;
                cpyCount -= startNdx;
            }

            cpyCount = cpyCount % blockSize;
            if (cpyCount == 0)
                cpyCount = blockSize;
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

            long retNdx = destOffset;
            for (int i = 0; i < blockKeys.Count; i++)
            {
                long startNdx, cpyCount;
                Program.GetMultiBlockCopyOffsets(i, blockKeys.Count, blockSize, offset, size, out startNdx, out cpyCount);

                ReadBlock(blockKeys[i], dest, retNdx, startNdx, cpyCount);
                retNdx += cpyCount;
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
    }


}
