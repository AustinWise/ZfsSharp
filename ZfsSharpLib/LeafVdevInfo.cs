using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZfsSharpLib.HardDisks;

namespace ZfsSharpLib
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

            // TODO: try reading all labels and use the one with the highest txg.
            var rentedBytes = Program.RentBytes(VDEV_PHYS_SIZE);
            if (!hdd.ReadLabelBytes(rentedBytes, VDEV_SKIP_SIZE))
                throw new Exception("Invalid checksum on label config data!");
            Config = new NvList(rentedBytes);
            Program.ReturnBytes(rentedBytes);
            rentedBytes = default;

            Txg = Config.Get<ulong>("txg");

            //figure out how big the uber blocks are
            var vdevTree = Config.Get<NvList>("vdev_tree");
            var ubShift = (int)vdevTree.Get<ulong>("ashift");
            ubShift = Math.Max(ubShift, UBERBLOCK_SHIFT);
            ubShift = Math.Min(ubShift, MAX_UBERBLOCK_SHIFT);
            var ubSize = 1 << ubShift;
            var ubCount = VDEV_UBERBLOCK_RING >> ubShift;

            List<uberblock_t> blocks = new List<uberblock_t>();
            var ubBytes = Program.RentBytes(ubSize);
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
            Program.ReturnBytes(ubBytes);
            ubBytes = default;


            bool foundUberblock = false;
            foreach (var b in blocks)
            {
                if (b.Txg == Txg)
                {
                    this.Uberblock = b;
                    foundUberblock = true;
                    break;
                }
            }
            if (!foundUberblock)
            {
                // TODO: recover from the case where the label config was written but the uberblock
                // was not yet written to the label.
                throw new Exception($"Config has txg {Txg}, but there was no matching uberblock.");
            }

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            hdd = OffsetHardDisk.Create(hdd, VDevLableSizeStart, hdd.Length - VDevLableSizeStart - VDevLableSizeEnd);
            this.HDD = hdd;
        }

        public ulong Guid { get; private set; }
        public ulong Txg { get; private set; }
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

        public static List<LeafVdevInfo> GetLeafVdevs(string dirOrFile)
        {
            var ret = new List<LeafVdevInfo>();

            try
            {
                FileInfo[] files;
                if (File.Exists(dirOrFile))
                {
                    files = new FileInfo[] { new FileInfo(dirOrFile) };
                }
                else
                {
                    files = new DirectoryInfo(dirOrFile).GetFiles();
                }

                foreach (var fi in files)
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
