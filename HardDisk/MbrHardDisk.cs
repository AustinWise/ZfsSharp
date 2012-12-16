using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace ZfsSharp.HardDisk
{
    enum MbrPartitionType : byte
    {
        Empty = 0,
        Ntfs = 7,
        GptProtective = 0xee,
    }
    class MbrHardDisk : IHardDisk
    {
        #region StructStuff
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct CHS
        {
            short Stuff1;
            byte Stuff2;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct PartitionEntry
        {
            public byte Status;
            public CHS FirstSector;
            public MbrPartitionType Type;
            public CHS LastSector;
            public uint FirstSectorLba;
            public uint NumberOfSectors;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct MbrHeader
        {
            fixed byte BootstrapCode1[218];
            short Zeros1;
            public byte OriginalPhysicalDrive;
            public byte Seconds;
            public byte Minutes;
            public byte Hours;
            fixed byte BootStrapCode2[216];
            public int DiskSig;
            short Zeros2;
            public PartitionEntry Partition1;
            public PartitionEntry Partition2;
            public PartitionEntry Partition3;
            public PartitionEntry Partition4;
            public ushort BootSig;

            public PartitionEntry GetPartition(int index)
            {
                switch (index)
                {
                    case 0:
                        return Partition1;
                    case 1:
                        return Partition2;
                    case 2:
                        return Partition3;
                    case 3:
                        return Partition4;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        const ushort MbrMagic = 0xaa55;
        const long SectorSize = 512;
        #endregion

        private IHardDisk mHdd;
        private MbrHeader mHeader;
        private PartitionEntry mPartition;
        private long mOffset;
        private long mSize;

        public MbrHardDisk(IHardDisk hdd, int partition)
        {
            this.mHdd = hdd;
            mHdd.Get(0, out mHeader);

            //for now, always assume a GPT partition
            if (!MbrHardDisk.IsMbr(hdd))
                throw new Exception("Expected a MBR hdd.");
            if (MbrHardDisk.GetType(hdd, 0) != MbrPartitionType.GptProtective)
                throw new Exception("Expected a GPT protective MBR entry.");

            mPartition = mHeader.GetPartition(partition);
            mOffset = (long)mPartition.FirstSectorLba * SectorSize;
            mSize = (long)mPartition.NumberOfSectors * SectorSize;
        }

        public static bool IsMbr(IHardDisk hdd)
        {
            MbrHeader h;
            hdd.Get(0, out h);
            return h.BootSig == MbrMagic;
        }

        public static MbrPartitionType GetType(IHardDisk hdd, int index)
        {
            MbrHeader h;
            hdd.Get(0, out h);
            return h.GetPartition(index).Type;
        }

        public void Get<T>(long offset, out T @struct) where T : struct
        {
            mHdd.Get<T>(offset + mOffset, out @struct);
        }

        public long Length
        {
            get { return mSize; }
        }
    }
}
