using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ZfsSharp.HardDisks;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
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

    class Program
    {
        static List<LeafVdevInfo> GetLeafVdevs(string dir)
        {
            var virtualHardDisks = new List<HardDisk>();
            foreach (var fi in new DirectoryInfo(dir).GetFiles("*.vhd"))
            {
                var file = new FileHardDisk(fi.FullName);
                var vhd = VhdHardDisk.Create(file);
                virtualHardDisks.Add(vhd);
            }
            foreach (var fi in new DirectoryInfo(dir).GetFiles("*.vdi"))
            {
                var file = new FileHardDisk(fi.FullName);
                var vhd = new VdiHardDisk(file);
                virtualHardDisks.Add(vhd);
            }

            var ret = new List<LeafVdevInfo>();
            foreach (var hdd in virtualHardDisks)
            {
                var gpt = new GptHardDrive(hdd);
                var vdev = new LeafVdevInfo(gpt);
                ret.Add(vdev);
            }
            foreach (var fi in new DirectoryInfo(dir).GetFiles("*.zfs"))
            {
                var file = new FileHardDisk(fi.FullName);
                var vdev = new LeafVdevInfo(file);
                ret.Add(vdev);
            }
            return ret;
        }

        static Vdev[] CreateVdevTree(List<LeafVdevInfo> hdds)
        {
            var poolGuid = hdds.Select(h => h.Config.Get<ulong>("pool_guid")).Distinct().Single();

            var hddMap = new Dictionary<ulong, LeafVdevInfo>();
            var innerVdevConfigs = new Dictionary<ulong, NvList>();
            foreach (var hdd in hdds)
            {
                hddMap.Add(hdd.Config.Get<ulong>("guid"), hdd);
                var vdevTree = hdd.Config.Get<NvList>("vdev_tree");
                innerVdevConfigs[vdevTree.Get<ulong>("guid")] = vdevTree;
            }

            var innerVdevs = new List<Vdev>();
            foreach (var kvp in innerVdevConfigs)
            {
                innerVdevs.Add(Vdev.Create(kvp.Value, hddMap));
            }

            ulong calculatedTopGuid = 0;
            for (int i = 0; i < innerVdevs.Count; i++)
            {
                calculatedTopGuid += innerVdevs[i].Guid;
            }

            var ret = innerVdevs.OrderBy(v => v.ID).ToArray();
            for (uint i = 0; i < ret.Length; i++)
            {
                if (ret[i].ID != i)
                    throw new Exception("Missing vdev.");
            }
            return ret;
        }

        static void Main(string[] args)
        {
            var hdds = GetLeafVdevs(@"D:\VPC\SmartOs4\");
            var vdevs = CreateVdevTree(hdds);

            Zio zio = new Zio(vdevs);
            var dmu = new Dmu(zio);
            var zap = new Zap(dmu);
            Dsl dsl = new Dsl(hdds[0].Uberblock.rootbp, zap, dmu, zio);

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

                if (ds.Key == "zones/var")
                    Console.WriteLine(Encoding.ASCII.GetString(zpl.GetFileContents(@"/svc/log/svc.startd.log")));
            }

            /*
             * TODO:
             *  DSL
             *  Complete Fat ZAP
             *  ZPL
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

        const string ROOT_DATASET = "root_dataset";
        const string CONFIG = "config";
    }


}
