using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZfsSharp.HardDisks;

namespace ZfsSharp
{
    class LeafVdevInfo
    {
        public LeafVdevInfo(HardDisk hdd)
        {
            this.HDD = hdd;

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

            using (var s = new MemoryStream(hdd.ReadBytes(16 << 10, 112 << 10)))
                Config = new NvList(s);
            if (Config.Get<ulong>("version") != 5000)
            {
                throw new NotSupportedException();
            }

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            hdd = OffsetHardDisk.Create(hdd, VDevLableSizeStart, hdd.Length - VDevLableSizeStart - VDevLableSizeEnd);
            this.HDD = hdd;
        }

        public ulong Guid { get; private set; }
        public uberblock_t Uberblock { get; private set; }
        public HardDisk HDD { get; private set; }
        public NvList Config { get; private set; }
    }
}
