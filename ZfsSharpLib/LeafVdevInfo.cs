using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZfsSharp.HardDisks;

namespace ZfsSharp
{
    class LeafVdevInfo : IDisposable
    {
        const int VDEV_PAD_SIZE = (8 << 10);
        /* 2 padding areas (vl_pad1 and vl_pad2) to skip */
        const int VDEV_SKIP_SIZE = VDEV_PAD_SIZE * 2;
        const int VDEV_PHYS_SIZE = (112 << 10);

        //uberblocks can be between 1k and 8k
        const int UBERBLOCK_SHIFT = 10;
        const int MAX_UBERBLOCK_SHIFT = 13;
        const int VDEV_UBERBLOCK_RING = (128 << 10);

        public LeafVdevInfo(HardDisk hdd)
        {
            this.HDD = hdd;

            var rentedBytes = Program.RentBytes(VDEV_PHYS_SIZE);

            try
            {
                if (!hdd.ReadLabelBytes(rentedBytes, VDEV_SKIP_SIZE))
                    throw new Exception("Invalid checksum on lable config data!");
                Config = new NvList(rentedBytes);
            }
            finally
            {
                Program.ReturnBytes(rentedBytes);
                rentedBytes = default(ArraySegment<byte>);
            }

            //figure out how big the uber blocks are
            var vdevTree = Config.Get<NvList>("vdev_tree");
            var ubShift = (int)vdevTree.Get<ulong>("ashift");
            ubShift = Math.Max(ubShift, UBERBLOCK_SHIFT);
            ubShift = Math.Min(ubShift, MAX_UBERBLOCK_SHIFT);
            var ubSize = 1 << ubShift;
            var ubCount = VDEV_UBERBLOCK_RING >> ubShift;

            List<uberblock_t> blocks = new List<uberblock_t>();
            var ubBytes = Program.RentBytes(ubSize);
            try
            {
                for (long i = 0; i < ubCount; i++)
                {
                    var offset = VDEV_SKIP_SIZE + VDEV_PHYS_SIZE + ubSize * i;
                    if (!hdd.ReadLabelBytes(ubBytes, offset))
                        continue;
                    uberblock_t b = Program.ToStruct<uberblock_t>(ubBytes.Array, ubBytes.Offset);
                    if (b.Magic == uberblock_t.UbMagic)
                    {
                        blocks.Add(b);
                    }
                }
            }
            finally
            {
                Program.ReturnBytes(ubBytes);
                ubBytes = default(ArraySegment<byte>);
            }
            this.Uberblock = blocks.OrderByDescending(u => u.Txg).ThenByDescending(u => u.TimeStamp).First();

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

        static readonly Dictionary<string, Func<FileHardDisk, HardDisk>> sFileFormats = new Dictionary<string, Func<FileHardDisk, HardDisk>>(StringComparer.OrdinalIgnoreCase)
        {
            { ".vhd",  fileHdd => new GptHardDrive(VhdHardDisk.Create(fileHdd)) },
            { ".vhdx", fileHdd => new GptHardDrive(new VhdxHardDisk(fileHdd)) },
            { ".vdi",  fileHdd => new GptHardDrive(new VdiHardDisk(fileHdd)) },
            { ".zfs",  fileHdd => fileHdd },
        };

        public static List<LeafVdevInfo> GetLeafVdevs(string dir)
        {
            var ret = new List<LeafVdevInfo>();

            try
            {
                foreach (var fi in new DirectoryInfo(dir).GetFiles())
                {
                    Func<FileHardDisk, HardDisk> factory;
                    if (!sFileFormats.TryGetValue(fi.Extension, out factory))
                        continue;

                    var file = new FileHardDisk(fi.FullName);
                    var partition = factory(file);
                    var vdev = new LeafVdevInfo(partition);
                    ret.Add(vdev);
                }
            }
            catch
            {
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
