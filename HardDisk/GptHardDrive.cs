using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ZfsSharp.HardDisk
{
    class GptHardDrive : IHardDisk
    {
        readonly static Guid SolarisUsrPartitionId = new Guid("6A898CC3-1DD2-11B2-99A6-080020736631");

        const string EfiMagic = "EFI PART";
        const int CurrentRevision = 0x00010000;
        const int CurrentHeaderSize = 92;
        const int ParitionEntrySize = 128;
        const long SectorSize = 512; //TODO: DEAL WITH IT
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct GptHeader
        {
            fixed byte signature[8];
            public int Revision;
            public int HeaderSize;
            public int Crc;
            int Zero1;
            public long CurrentLba;
            public long BackupLba;
            public long FirstUsableLba;
            public long LastUsableLba;
            public Guid DiskGuid;
            public long StartingLbaOfPartitionEntries;
            public int NumberOfPartitions;
            public int SizeOfPartitionEntry;
            public int CrcOfPartitionEntry;

            public string Signature
            {
                get
                {
                    fixed (byte* bytes = signature)
                        return Marshal.PtrToStringAnsi(new IntPtr(bytes), 8);
                }
            }
        }

        [Flags]
        enum PartitionAttributes : long
        {
            None = 0,
            System = 1,
            Active = 1 << 2,
            ReadOnly = 1 << 60,
            Hidden = 1 << 62,
            DoNotAutomount = 1 << 63,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct PartitionEntry
        {
            const int NameSize = 72;

            //TODO: in all liklyhood the layout of a GUID is stable, but we should check anyways
            public Guid Type;
            public Guid ID;
            public long FirstLba;
            public long LastLba;
            public PartitionAttributes Attributes;
            fixed byte name[NameSize];

            public string Name
            {
                get
                {
                    byte[] copiedBytes = new byte[NameSize];
                    fixed (byte* bytes = name)
                    {
                        Marshal.Copy(new IntPtr(bytes), copiedBytes, 0, NameSize);
                    }
                    string ret = Encoding.GetEncoding("utf-16").GetString(copiedBytes, 0, NameSize);
                    int subStr = ret.IndexOf('\0');
                    if (subStr != -1)
                        ret = ret.Substring(0, subStr);
                    return ret;
                }
            }
        }

        private IHardDisk mHdd;
        private GptHeader mHeader;
        private PartitionEntry mPartition;
        private long mOffset;
        private long mSize;

        public GptHardDrive(IHardDisk hdd)
        {
            mHdd = hdd;
            mHdd.Get(0, out mHeader);
            if (mHeader.Signature != EfiMagic)
                throw new Exception("Not a GPT.");
            if (mHeader.Revision != CurrentRevision)
                throw new Exception("Wrong rev.");
            if (mHeader.HeaderSize != SectorSize)
                throw new Exception("Wrong header size.");
            //TODO: check crc
            if (mHeader.SizeOfPartitionEntry != ParitionEntrySize)
                throw new Exception("Wrong ParitionEntrySize.");
            //TODO: check partition entry CRC

            if (mHeader.NumberOfPartitions == 0)
                throw new Exception("No partitions!");

            List<PartitionEntry> parts = new List<PartitionEntry>();
            for (int i = 0; i < mHeader.NumberOfPartitions; i++)
            {
                parts.Add(GetLba<PartitionEntry>(mHeader.StartingLbaOfPartitionEntries, i * mHeader.SizeOfPartitionEntry));
            }

            //TODO: don't hard code this
            mPartition = parts[0];
            if (mPartition.Type != SolarisUsrPartitionId || mPartition.Name != "zfs")
                throw new Exception("Not a ZFS partition.");

            mOffset = SectorSize * (mPartition.FirstLba - mHeader.CurrentLba);
            mSize = SectorSize * (mPartition.LastLba - mPartition.FirstLba);
        }

        private T GetLba<T>(long absoluteLba, long extraOffset) where T : struct
        {
            long relativeLba = absoluteLba - mHeader.CurrentLba;
            long byteOffset = relativeLba * SectorSize + extraOffset;
            T ret;
            mHdd.Get<T>(byteOffset, out ret);
            return ret;
        }

        public void Get<T>(long offset, out T @struct) where T : struct
        {
            mHdd.Get<T>(mOffset + offset, out @struct);
        }

        public long Length
        {
            get { return SectorSize * (mPartition.LastLba - mPartition.FirstLba); }
        }
    }
}
