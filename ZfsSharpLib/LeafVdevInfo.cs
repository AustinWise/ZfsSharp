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
                var bytes = hdd.ReadLabelBytes(offset, 1024);
                if (bytes == null)
                    continue;
                uberblock_t b = Program.ToStruct<uberblock_t>(bytes);
                if (b.Magic == uberblock_t.UbMagic)
                {
                    blocks.Add(b);
                }
            }
            this.Uberblock = blocks.OrderByDescending(u => u.Txg).First();

            Config = new NvList(hdd.ReadLabelBytes(16 << 10, 112 << 10));

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
            new FileFormatInfo("vhd", fileHdd => new GptHardDrive(VhdHardDisk.Create(fileHdd))),
            new FileFormatInfo("vdi", fileHdd => new GptHardDrive(new VdiHardDisk(fileHdd))),
            new FileFormatInfo("zfs", fileHdd => fileHdd),
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
