using System;
using System.Collections.Generic;
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

            Config = new NvList(hdd.ReadLabelBytes(VDEV_SKIP_SIZE, VDEV_PHYS_SIZE));

            //figure out how big the uber blocks are
            var vdevTree = Config.Get<NvList>("vdev_tree");
            var ubShift = (int)vdevTree.Get<ulong>("ashift");
            ubShift = Math.Max(ubShift, UBERBLOCK_SHIFT);
            ubShift = Math.Min(ubShift, MAX_UBERBLOCK_SHIFT);
            var ubSize = 1 << ubShift;
            var ubCount = VDEV_UBERBLOCK_RING >> ubShift;

            List<uberblock_t> blocks = new List<uberblock_t>();
            for (long i = 0; i < ubCount; i++)
            {
                var offset = VDEV_SKIP_SIZE + VDEV_PHYS_SIZE + ubSize * i;
                var bytes = hdd.ReadLabelBytes(offset, ubSize);
                if (bytes == null)
                    continue;
                uberblock_t b = Program.ToStruct<uberblock_t>(bytes);
                if (b.Magic == uberblock_t.UbMagic)
                {
                    blocks.Add(b);
                }
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

        class FileFormatInfo
        {
            public FileFormatInfo(string fileExt, Func<FileHardDisk, HardDisk> findZfsPart)
            {
                this.FileExtension = fileExt;
                this.FindZfsPartition = findZfsPart;
            }

            public string FileExtension { get; }
            public Func<FileHardDisk, HardDisk> FindZfsPartition { get; }
        }

        static readonly List<FileFormatInfo> sFileFormats = new List<FileFormatInfo>()
        {
            new FileFormatInfo("vhd",  fileHdd => new GptHardDrive(VhdHardDisk.Create(fileHdd))),
            new FileFormatInfo("vhdx", fileHdd => new GptHardDrive(new VhdxHardDisk(fileHdd))),
            new FileFormatInfo("vdi",  fileHdd => new GptHardDrive(new VdiHardDisk(fileHdd))),
            new FileFormatInfo("zfs",  fileHdd => fileHdd),
        };

        public static List<LeafVdevInfo> GetLeafVdevs(string dir)
        {
            var ret = new List<LeafVdevInfo>();

            try
            {
                foreach (var fileFormat in sFileFormats)
                {
                    foreach (var fi in new DirectoryInfo(dir).GetFiles("*." + fileFormat.FileExtension))
                    {
                        var file = new FileHardDisk(fi.FullName);
                        var partition = fileFormat.FindZfsPartition(file);
                        var vdev = new LeafVdevInfo(partition);
                        ret.Add(vdev);
                    }
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
