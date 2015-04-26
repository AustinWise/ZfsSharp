using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;

namespace ZfsSharp.HardDisks
{
    enum MbrPartitionType : byte
    {
        Empty = 0,
        Ntfs = 7,
        Solaris = 0xbf,
        GptProtective = 0xee,
    }
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
        public const ushort MbrMagic = 0xaa55;
        public const long SectorSize = 512;

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
    #endregion

    class MbrHardDisk : OffsetHardDisk
    {
        private MbrHeader mHeader;
        private PartitionEntry mPartition;

        public MbrHardDisk(HardDisk hdd, int partition)
        {
            hdd.Get(0, out mHeader);

            //for now, always assume a GPT partition
            if (!MbrHardDisk.IsMbr(hdd))
                throw new Exception("Expected a MBR hdd.");
            if (MbrHardDisk.GetType(hdd, 0) != MbrPartitionType.GptProtective)
                throw new Exception("Expected a GPT protective MBR entry.");

            mPartition = mHeader.GetPartition(partition);
            Init(hdd, (long)mPartition.FirstSectorLba * MbrHeader.SectorSize, (long)mPartition.NumberOfSectors * MbrHeader.SectorSize);
        }

        public static bool IsMbr(HardDisk hdd)
        {
            MbrHeader h;
            hdd.Get(0, out h);
            return h.BootSig == MbrHeader.MbrMagic;
        }

        public static MbrPartitionType GetType(HardDisk hdd, int index)
        {
            MbrHeader h;
            hdd.Get(0, out h);
            return h.GetPartition(index).Type;
        }
    }
}
