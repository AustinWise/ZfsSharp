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
            var vhd = new VhdHardDisk(@"D:\VPC\SmartOs\SmartOs.vhd");
            //var vhd = new VhdHardDisk(@"d:\VPC\SmartOs3\SmartOs3.vhd");
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

            var rootbp = ub.rootbp;

            objset_phys_t mos = zio.Get<objset_phys_t>(rootbp);

            dnode_phys_t objectDirectory = dmu.ReadFromObjectSet(mos, 1);
            var objDir = zap.Parse(objectDirectory);

            var configDn = dmu.ReadFromObjectSet(mos, objDir[CONFIG]);
            var confginNv = new NvList(new MemoryStream(dmu.Read(configDn)));

            var rootDslObj = dmu.ReadFromObjectSet(mos, objDir[ROOT_DATASET]);
            var rootDsl = dmu.GetBonus<dsl_dir_phys_t>(rootDslObj);

            var rootDataSetObj = dmu.ReadFromObjectSet(mos, rootDsl.head_dataset_obj);
            var rootDataSet = dmu.GetBonus<dsl_dataset_phys_t>(rootDataSetObj);

            var rootZfs = zio.Get<objset_phys_t>(rootDataSet.bp);
            var rootZfaObjDir = zap.Parse(dmu.ReadFromObjectSet(rootZfs, 1));
            if (rootZfaObjDir["VERSION"] != 5)
                throw new NotSupportedException();

            var saAttrsDn = dmu.ReadFromObjectSet(rootZfs, rootZfaObjDir["SA_ATTRS"]);
            var saAttrs = zap.Parse(saAttrsDn);
            var saLayouts = zap.Parse(dmu.ReadFromObjectSet(rootZfs, saAttrs["LAYOUTS"]));


            var rootDirDnode = dmu.ReadFromObjectSet(rootZfs, rootZfaObjDir["ROOT"]);
            var rootSaHeader = dmu.GetBonus<sa_hdr_phys_t>(rootDirDnode);
            var rootDirContents = zap.Parse(rootDirDnode);

            var fileId = rootDirContents["currbooted"];
            var fileDn = dmu.ReadFromObjectSet(rootZfs, fileId);
            var fileSa = dmu.GetBonus<sa_hdr_phys_t>(fileDn);
            var bytes = Encoding.ASCII.GetString(dmu.Read(fileDn));

            /*
             * TODO:
             *  DSL
             *  Fat ZAP
             *  ZPL
             */

            Console.WriteLine();
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
