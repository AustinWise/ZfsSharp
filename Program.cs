using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ZfsSharp.HardDisk;

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
            //var vhd = new VhdHardDisk(@"D:\VPC\SmartOs\SmartOs.vhd");
            var vhd = new VhdHardDisk(@"d:\VPC\SmartOs3\SmartOs3.vhd");
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
            using (var s = gpt.GetStream(16 << 10, 112 << 10))
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
            var fileContents = Encoding.ASCII.GetString(rootZpl.GetFileContents("/currbooted"));
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

        public const int SPA_MINBLOCKSHIFT = 9;
        public const int SPA_MAXBLOCKSHIFT = 17;
        const long SPA_MINBLOCKSIZE = (1L << SPA_MINBLOCKSHIFT);
        const long SPA_MAXBLOCKSIZE = (1L << SPA_MAXBLOCKSHIFT);

        const string ROOT_DATASET = "root_dataset";
        const string CONFIG = "config";
    }


}
