using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZfsSharp.HardDisks;

namespace ZfsSharp
{
    class LeafVdevInfo : IDisposable
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

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            hdd = OffsetHardDisk.Create(hdd, VDevLableSizeStart, hdd.Length - VDevLableSizeStart - VDevLableSizeEnd);
            this.HDD = hdd;
        }

        public ulong Guid { get; private set; }
        public uberblock_t Uberblock { get; private set; }
        public HardDisk HDD { get; private set; }
        public NvList Config { get; private set; }

        public void Dispose()
        {
            HDD.Dispose();
        }

        public static List<LeafVdevInfo> GetLeafVdevs(string dir)
        {
            var virtualHardDisks = new List<HardDisk>();
            var ret = new List<LeafVdevInfo>();

            try
            {
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
            }
            catch
            {
                foreach (var hdd in virtualHardDisks)
                {
                    hdd.Dispose();
                }
                foreach (var leaf in ret)
                {
                    leaf.Dispose();
                }
                throw;
            }

            return ret;
        }
    }
}
