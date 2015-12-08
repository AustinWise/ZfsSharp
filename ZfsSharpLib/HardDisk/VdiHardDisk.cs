using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp.HardDisks
{
    class VdiHardDisk : HardDisk
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

        readonly HardDisk mHdd;
        readonly long mDataOffset;
        readonly int mBlockSize;
        readonly uint[] mBlockLocations;

        unsafe public VdiHardDisk(HardDisk hdd)
        {
            this.mHdd = hdd;
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

            mBlockLocations = new uint[head.BlocksInHdd];
            for (long i = 0; i < head.BlocksInHdd; i++)
            {
                hdd.Get<uint>(head.OffsetBlocks + i * 4, out mBlockLocations[i]);
            }
            mDataOffset = head.OffsetData;
            mBlockSize = (int)head.BlockSize;
        }

        public override void Get<T>(long offset, out T @struct)
        {
            var bytes = this.ReadBytes(offset, Marshal.SizeOf(typeof(T)));
            @struct = Program.ToStruct<T>(bytes);
        }

        public override void ReadBytes(byte[] array, int arrayOffset, long offset, int count)
        {
            Program.MultiBlockCopy<long>(array, arrayOffset, offset, count, mBlockSize, getBlockOffset, readBlock);
        }

        private long getBlockOffset(long blockId)
        {
            uint blockOffset = mBlockLocations[blockId];
            if (blockOffset == ~0u)
                return -1;
            return mDataOffset + blockOffset * mBlockSize;
        }

        void readBlock(long blockOffset, byte[] array, int arrayOffset, int blockStartNdx, int blockCpyCount)
        {
            if (blockOffset == -1)
            {
                for (int i = 0; i < blockCpyCount; i++)
                {
                    array[arrayOffset + i] = 0;
                }
            }
            else
            {
                mHdd.ReadBytes(array, arrayOffset, blockOffset + blockStartNdx, blockCpyCount);
            }
        }

        public override long Length
        {
            get { return this.mBlockSize * mBlockLocations.LongLength; }
        }

        public override void Dispose()
        {
            mHdd.Dispose();
        }
    }
}
