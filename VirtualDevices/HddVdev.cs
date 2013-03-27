using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ZfsSharp.HardDisks;

namespace ZfsSharp.VirtualDevices
{
    class HddVdev : Vdev
    {
        readonly HardDisk mHdd;
        public HddVdev(HardDisk hdd)
        {
            this.mHdd = hdd;

            List<uberblock_t> blocks = new List<uberblock_t>();
            for (long i = 0; i < 128; i++)
            {
                var offset = (128 << 10) + 1024 * i;
                uberblock_t b;
                hdd.Get<uberblock_t>(offset, out b);
                if (b.Magic == uberblock_t.UbMagic)
                    blocks.Add(b);
            }
            this.Uberblock = blocks.OrderByDescending(u => u.Txg).First();

            NvList nv;
            using (var s = new MemoryStream(hdd.ReadBytes(16 << 10, 112 << 10)))
                nv = new NvList(s);
            if (nv.Get<ulong>("version") != 5000)
            {
                throw new NotSupportedException();
            }
            Guid = nv.Get<UInt64>("guid");

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            hdd = new OffsetHardDisk(hdd, VDevLableSizeStart, hdd.Length - VDevLableSizeStart - VDevLableSizeEnd);
            this.mHdd = hdd;
        }

        public override IEnumerable<byte[]> ReadBytes(long offset, long count)
        {
            yield return mHdd.ReadBytes(offset, count);
        }

        public ulong Guid { get; private set; }
        public uberblock_t Uberblock { get; private set; }
    }
}
