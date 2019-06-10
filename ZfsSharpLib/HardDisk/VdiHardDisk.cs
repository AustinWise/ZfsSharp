using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZfsSharpLib.HardDisks
{
    class VdiHardDisk : OffsetTableHardDisk
    {
        enum ImageType : uint
        {
            Dynamic = 0x01,
            Fixed = 0x02,
        }

        const uint VdiMagic = 0xbeda107f;
        const uint VdiHeadSize = 0x190;
        const uint VdiVersion = 0x00010001;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct VdiHeader
        {
            fixed byte Text[0x40];

            public uint ImageSig;
            public uint Version;
            public uint SizeOfHeader;
            public ImageType ImageType;

            public uint ImageFlags;
            fixed byte ImageDescription[0x100];
            public uint OffsetBlocks;
            public uint OffsetData;
            uint Cylinders;

            uint Heads;
            uint Sectors;
            public uint SectorSize;
            uint pad;

            public ulong DiskSizeInBytes;
            public uint BlockSize;
            uint BlockExtraData;

            public uint BlocksInHdd;
            public uint BlocksAllocated;
            public Guid Uuid;
            public Guid UuidOfLastSnap;
            public Guid UuidLink;
            public Guid UuidParent;
        }

        unsafe public VdiHardDisk(HardDisk hdd)
            : base(hdd)
        {
            var headBytes = hdd.ReadBytes(0, sizeof(VdiHeader));
            VdiHeader head = Program.ToStruct<VdiHeader>(headBytes);

            if (head.ImageSig != VdiMagic)
                throw new Exception("Wrong magic.");
            if (head.Version != VdiVersion)
                throw new Exception("Wrong version.");
            if (head.SizeOfHeader != VdiHeadSize)
                throw new Exception("Wrong size.");

            if (head.ImageType != ImageType.Dynamic)
                throw new NotImplementedException("Only dynamic is supported.");

            var dataOffset = head.OffsetData;
            mBlockOffsets = new long[head.BlocksInHdd];
            mBlockSize = (int)head.BlockSize;

            for (long i = 0; i < head.BlocksInHdd; i++)
            {
                uint blockLoc;
                hdd.Get<uint>(head.OffsetBlocks + i * 4, out blockLoc);
                if (blockLoc == ~0u)
                    mBlockOffsets[i] = -1;
                else
                    mBlockOffsets[i] = dataOffset + blockLoc * mBlockSize;
            }
        }
    }
}
