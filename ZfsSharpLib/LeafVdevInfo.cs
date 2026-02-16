#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using ZfsSharpLib.HardDisks;

namespace ZfsSharpLib
{
    public class LeafVdevInfo : IDisposable
    {
        const int VDEV_PAD_SIZE = (8 << 10);
        /* 2 padding areas (vl_pad1 and vl_pad2) to skip */
        const int VDEV_SKIP_SIZE = VDEV_PAD_SIZE * 2;
        const int VDEV_PHYS_SIZE = (112 << 10);

        //uberblocks can be between 1k and 8k
        const int UBERBLOCK_SHIFT = 10;
        const int MAX_UBERBLOCK_SHIFT = 13;
        const int VDEV_UBERBLOCK_RING = (128 << 10);
        const int VDEV_LABELS = 4;

        const int VDEV_LABEL_TOTAL_SIZE = VDEV_SKIP_SIZE + VDEV_PHYS_SIZE + VDEV_UBERBLOCK_RING;

        private LeafVdevInfo(HardDisk hdd)
        {
            Debug.Assert(VDEV_LABEL_TOTAL_SIZE == 256 << 10);

            var configs = new List<(ulong txg, NvList config)>(4);
            for (int i = 0; i < VDEV_LABELS; i++)
            {
                if (TryReadConfig(hdd, i, out var config))
                {
                    ulong txg = config.Get<ulong>("txg");
                    configs.Add(((ulong)i, config));
                }
            }

            if (configs.Count == 0)
            {
                throw new Exception("No valid vdev label found.");
            }

            // Sort by txg descending.
            configs.Sort((a, b) => b.txg.CompareTo(a.txg));

            int ashift = (int)configs[0].config.Get<NvList>("vdev_tree").Get<ulong>("ashift");

            uberblock_t bestUberblock = default;
            for (int i = 0; i < VDEV_LABELS; i++)
            {
                FindBestUberblock(hdd, ashift, i, ref bestUberblock);
            }

            if (bestUberblock.Txg == 0)
            {
                throw new Exception("No valid uberblock found.");
            }

            foreach (var c in configs)
            {
                if (c.txg <= bestUberblock.Txg)
                {
                    this.Config = c.config;
                    this.Guid = c.config.Get<ulong>("guid");
                    this.ConfigTxg = c.txg;
                    break;
                }
            }

            if (Config is null)
            {
                throw new Exception("No vdev config found that is older than or equal to the best uberblock.");
            }

            this.Txg = bestUberblock.Txg;
            this.Uberblock = bestUberblock;

            const int VDevLableSizeStart = 4 << 20;
            const int VDevLableSizeEnd = 512 << 10;
            hdd = OffsetHardDisk.Create(hdd, VDevLableSizeStart, hdd.Length - VDevLableSizeStart - VDevLableSizeEnd);
            this.HDD = hdd;
        }

        internal ulong Guid { get; private set; }
        internal ulong ConfigTxg { get; private set; }
        internal ulong Txg { get; private set; }
        internal uberblock_t Uberblock { get; private set; }
        internal HardDisk HDD { get; private set; }
        internal NvList Config { get; private set; }

        public void Dispose()
        {
            HDD.Dispose();
        }

        private static long GetVdevLabelOffset(HardDisk hdd, int labelIndex)
        {
            Debug.Assert(labelIndex >= 0 && labelIndex < 4);
            long labelStart = labelIndex < 2 ? labelIndex * VDEV_LABEL_TOTAL_SIZE : hdd.Length - VDEV_LABEL_TOTAL_SIZE * (VDEV_LABELS - labelIndex);
            return labelStart;
        }

        private static bool TryReadConfig(HardDisk hdd, int labelIndex, [NotNullWhen(true)] out NvList? config)
        {
            config = null;

            Debug.Assert(labelIndex >= 0 && labelIndex < 4);
            long labelStart = GetVdevLabelOffset(hdd, labelIndex);

            var rentedBytes = Program.RentBytes(VDEV_PHYS_SIZE);
            if (!hdd.ReadLabelBytes(rentedBytes, labelStart + VDEV_SKIP_SIZE))
            {
                return false;
            }
            config = new NvList(rentedBytes);
            Program.ReturnBytes(rentedBytes);
            rentedBytes = default;

            return true;
        }

        private static void FindBestUberblock(HardDisk hdd, int ashift, int labelNdx, ref uberblock_t bestUberblock)
        {
            long labelStart = GetVdevLabelOffset(hdd, labelNdx);
            var ubShift = Math.Max(ashift, UBERBLOCK_SHIFT);
            ubShift = Math.Min(ubShift, MAX_UBERBLOCK_SHIFT);
            var ubSize = 1 << ubShift;
            var ubCount = VDEV_UBERBLOCK_RING >> ubShift;

            var ubBytes = Program.RentBytes(ubSize);
            for (long i = 0; i < ubCount; i++)
            {
                var offset = labelStart + VDEV_SKIP_SIZE + VDEV_PHYS_SIZE + ubSize * i;
                if (!hdd.ReadLabelBytes(ubBytes, offset))
                    continue;
                uberblock_t b = Program.ToStruct<uberblock_t>(ubBytes.Array, ubBytes.Offset);
                if (b.Magic == uberblock_t.UbMagic && b.CompareTo(bestUberblock) > 0)
                {
                    bestUberblock = b;
                }
            }
            Program.ReturnBytes(ubBytes);
        }

        static readonly Dictionary<string, Func<HardDisk, HardDisk>> sFileFormats = new Dictionary<string, Func<HardDisk, HardDisk>>(StringComparer.OrdinalIgnoreCase)
        {
            { ".vhd",  fileHdd => new GptHardDrive(VhdHardDisk.Create(fileHdd)) },
            { ".vhdx", fileHdd => new GptHardDrive(new VhdxHardDisk(fileHdd)) },
            { ".vdi",  fileHdd => new GptHardDrive(new VdiHardDisk(fileHdd)) },
        };

        /// <summary>
        /// Creates a LeafVdevInfo from a file. If the file has known disk image format, then we will
        /// search for the GPT partition that contains a ZFS partition and open that. Otherwise, we
        /// will treat it as a leaf ZFS vdev in a file or a block device.
        /// </summary>
        /// <param name="fi"></param>
        /// <returns></returns>
        public static LeafVdevInfo CreateFromFile(FileInfo fi)
        {
            HardDisk hdd = new FileHardDisk(fi.FullName);
            Func<HardDisk, HardDisk>? factory;
            if (sFileFormats.TryGetValue(fi.Extension, out factory))
            {
                hdd = factory(hdd);
            }
            return new LeafVdevInfo(hdd);
        }
    }
}
