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
            var diskGuid = nv.Get<UInt64>("guid");

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            var dev = new OffsetHardDisk(gpt, VDevLableSizeStart, gpt.Length - VDevLableSizeStart - VDevLableSizeEnd);
            var zio = new Zio(new[] { dev });
            var dmu = new Dmu(zio);
            var zap = new Zap(dmu);

            var rootbp = ub.rootbp;

            byte[] logicalBytes = zio.Read(rootbp);

            objset_phys_t dn;
            fixed (byte* ptr = logicalBytes)
            {
                dn = (objset_phys_t)Marshal.PtrToStructure(new IntPtr(ptr), typeof(objset_phys_t));
            }

            dnode_phys_t objectDirectory = dmu.ReadFromObjectSet(dn.os_meta_dnode, 1);

            var objDir = zap.Parse(objectDirectory);

            var root = dmu.ReadFromObjectSet(dn.os_meta_dnode, objDir[ROOT_DATASET]);

            
            
            /*
             * TODO:
             *  DSL
             *  Indirect blocks
             *  Fat ZAP
             */


            Console.WriteLine();
        }

        public const int SPA_MINBLOCKSHIFT = 9;
        public const int SPA_MAXBLOCKSHIFT = 17;
        const long SPA_MINBLOCKSIZE = (1L << SPA_MINBLOCKSHIFT);
        const long SPA_MAXBLOCKSIZE = (1L << SPA_MAXBLOCKSHIFT);

        const string ROOT_DATASET = "root_dataset";
    }
}
