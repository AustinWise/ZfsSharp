using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Collections;

namespace ZfsSharp.HardDisks
{
    static class VhdHardDisk
    {
        #region Struct Crap
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
                throw new Exception();
            if (head.FileFormatVersion != 0x00010000)
                throw new Exception();

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

        class DynamicVhd : HardDisk
        {
            long mSize;
            HardDisk mHdd;
            int mBlockSize;
            int[] mBat;
            public DynamicVhd(HardDisk hdd)
            {
                VhdHeader head = GetHeader(hdd);
                int dySize = Marshal.SizeOf(typeof(DynamicHeader));
                DynamicHeader dyhead = Program.ToStructByteSwap<DynamicHeader>(hdd.ReadBytes(head.DataOffset, dySize));
                if (dyhead.CookieStr != "cxsparse")
                    throw new Exception();
                if (dyhead.HeaderVersion != 0x00010000)
                    throw new NotSupportedException();

                mHdd = hdd;
                mSize = head.CurrentSize;
                mBlockSize = dyhead.BlockSize;

                int numberOfBlocks = (int)(mSize / mBlockSize);
                if (numberOfBlocks > dyhead.MaxTableEntries)
                    throw new Exception();
                mBat = new int[numberOfBlocks];

                var bat = mHdd.ReadBytes(dyhead.TableOffset, numberOfBlocks * 4);

                for (int i = 0; i < numberOfBlocks; i++)
                {
                    Program.ByteSwap(typeof(int), bat, i * 4);
                    mBat[i] = Program.ToStruct<int>(bat, i * 4);
                }
            }

            public override void Get<T>(long offset, out T @struct)
            {
                @struct = Program.ToStruct<T>(ReadBytes(offset, Marshal.SizeOf(typeof(T))));
            }

            public override void ReadBytes(byte[] array, long arrayOffset, long offset, long count)
            {
                var bytes = ReadBytes(offset, count);
                Program.LongBlockCopy(bytes, 0, array, arrayOffset, count);
            }

            public override byte[] ReadBytes(long offset, long size)
            {
                CheckOffsets(offset, size);

                var blockOffsets = new List<long>();
                for (long i = offset; i < (offset + size); i += mBlockSize)
                {
                    long blockId = i / mBlockSize;
                    long blockOffset = mBat[blockId];
                    if (blockOffset == 0xffffffffL)
                        throw new Exception("Missing block.");
                    blockOffsets.Add(blockOffset * 512);
                }

                var ret = new byte[size];
                long retNdx = 0;
                for (int i = 0; i < blockOffsets.Count; i++)
                {
                    long startNdx, cpyCount;
                    Program.GetMultiBlockCopyOffsets(i, blockOffsets.Count, mBlockSize, offset, size, out startNdx, out cpyCount);

                    if (retNdx > Int32.MaxValue || startNdx > Int32.MaxValue || cpyCount > Int32.MaxValue)
                        throw new NotImplementedException("No support for blocks this big yet.");

                    var bytes = readBlock(blockOffsets[i]);
                    Buffer.BlockCopy(bytes, (int)startNdx, ret, (int)retNdx, (int)cpyCount);
                    retNdx += mBlockSize;
                }

                return ret;
            }

            byte[] readBlock(long blockOffset)
            {
                const int SECTOR_SIZE = 512;
                var numberOfSectors = mBlockSize / SECTOR_SIZE;
                var ba = new BitArray(mHdd.ReadBytes(blockOffset, numberOfSectors / 8));

                blockOffset += numberOfSectors / 8;

                var ret = new byte[mBlockSize];
                for (int i = 0; i < numberOfSectors; i++)
                {
                    if (ba[i])
                    {
                        var temp = mHdd.ReadBytes(blockOffset + i * SECTOR_SIZE, SECTOR_SIZE);
                        Buffer.BlockCopy(temp, 0, ret, i * SECTOR_SIZE, SECTOR_SIZE);
                    }
                }
                return ret;
            }

            public override long Length
            {
                get { return mSize; }
            }
        }
    }
}
