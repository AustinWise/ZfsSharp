using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace ZfsSharp.HardDisks
{
    static class VhdHardDisk
    {
        #region Structs
        [Flags]
        enum Features : int
        {
            None = 0,
            Temp = 1,
            Reserved = 2,
        }
        enum CreatorOs : int
        {
            Win = 0x5769326B, //(Wi2k)
            Max = 0x4D616320,
        }
        enum DiskType : int
        {
            None = 0,
            Fixed = 2,
            Dynamic = 3,
            Differencing = 4,
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct VhdHeader
        {
            public fixed byte Cookie[8];
            public Features Features;
            public int FileFormatVersion;
            public long DataOffset;
            public int TimeStamp;
            public fixed byte CreatorApp[4];
            public int CreatorVersion;
            public CreatorOs CreatorOs;
            public long OriginalSize;
            public long CurrentSize;
            public int DiskGeometry;
            public DiskType DiskType;
            public int Checksum;
            public fixed byte UniqueId[16];
            public byte SavedState;
            fixed byte Reserved[427];

            public string CookieStr
            {
                get
                {
                    fixed (byte* bytes = Cookie)
                    {
                        return Marshal.PtrToStringAnsi(new IntPtr(bytes), 8);
                    }
                }
            }

            public string CreatorAppStr
            {
                get
                {
                    fixed (byte* bytes = CreatorApp)
                    {
                        return Marshal.PtrToStringAnsi(new IntPtr(bytes), 4);
                    }
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct DynamicHeader
        {
            fixed byte Cookie[8];
            public ulong DataOffset;
            public long TableOffset;
            public int HeaderVersion;
            public int MaxTableEntries;
            public int BlockSize;
            public int Checksum;
            public Guid ParentUniqueID;
            public int ParentTimeStamp;
            int Reserved;
            fixed byte ParentUnicodeName[512];
            fixed byte LocatorEntries[8 * 24]; // for differencing disks only
            fixed byte Reserved2[256];

            public string CookieStr
            {
                get
                {
                    fixed (byte* bytes = Cookie)
                    {
                        return Marshal.PtrToStringAnsi(new IntPtr(bytes), 8);
                    }
                }
            }

            public string ParentUnicodeNameStr
            {
                get
                {
                    fixed (byte* bytes = ParentUnicodeName)
                    {
                        return Marshal.PtrToStringAnsi(new IntPtr(bytes), 512);
                    }
                }
            }
        }
        #endregion

        static VhdHeader GetHeader(HardDisk hdd)
        {
            return Program.ToStructByteSwap<VhdHeader>(hdd.ReadBytes(hdd.Length - 512, 512));
        }

        public static HardDisk Create(HardDisk hdd)
        {
            VhdHeader head = GetHeader(hdd);
            if (head.CookieStr != "conectix")
                throw new Exception("missing magic string");
            if (head.FileFormatVersion != 0x00010000)
                throw new Exception("upsupported version");

            if (head.DiskType == DiskType.Fixed)
            {
                return new FixedVhd(hdd);
            }
            else if (head.DiskType == DiskType.Dynamic)
            {
                return new DynamicVhd(hdd);
            }
            else
            {
                throw new Exception("Only fixed size VHDs are supported.");
            }
        }

        class FixedVhd : OffsetHardDisk
        {
            public FixedVhd(HardDisk hdd)
            {
                long size = hdd.Length - 512;
                var head = GetHeader(hdd);

                if (head.CurrentSize != size)
                    throw new Exception();

                Init(hdd, 0, size);
            }
        }

        class DynamicVhd : OffsetTableHardDisk
        {
            const int SECTOR_SIZE = 512;

            long mSize;

            public DynamicVhd(HardDisk hdd)
                : base(hdd)
            {
                VhdHeader head = GetHeader(hdd);
                int dySize = Program.SizeOf<DynamicHeader>();
                DynamicHeader dyhead = Program.ToStructByteSwap<DynamicHeader>(hdd.ReadBytes(head.DataOffset, dySize));
                if (dyhead.CookieStr != "cxsparse")
                    throw new Exception("missing magic string");
                if (dyhead.HeaderVersion != 0x00010000)
                    throw new NotSupportedException("wrong version");
                if (dyhead.ParentUniqueID != Guid.Empty)
                    throw new NotSupportedException("Differencing disks not supported.");

                mSize = head.CurrentSize;
                mBlockSize = dyhead.BlockSize;
                int sectorBitmapSize = (mBlockSize / SECTOR_SIZE) / 8;

                int numberOfBlocks = (int)(mSize / mBlockSize);
                if (numberOfBlocks > dyhead.MaxTableEntries)
                    throw new Exception();
                mBlockOffsets = new long[numberOfBlocks];

                var bat = hdd.ReadBytes(dyhead.TableOffset, numberOfBlocks * 4);

                for (int i = 0; i < numberOfBlocks; i++)
                {
                    Program.ByteSwap(typeof(int), bat, i * 4);
                    long batEntry = Program.ToStruct<int>(bat, i * 4);
                    if (batEntry != -1)
                    {
                        batEntry *= SECTOR_SIZE;
                        //skip the sector bitmap, since we don't support differencing disks
                        batEntry += sectorBitmapSize;
                    }
                    mBlockOffsets[i] = batEntry;
                }
            }

            public override long Length
            {
                get { return mSize; }
            }
        }
    }
}
