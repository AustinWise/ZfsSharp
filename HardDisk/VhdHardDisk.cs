using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ZfsSharp.HardDisk
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
            public long DataOffset;
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

        static VhdHeader GetHeader(IHardDisk hdd)
        {
            return Program.ToStructByteSwap<VhdHeader>(hdd.ReadBytes(hdd.Length - 512, 512));
        }

        //TODO: move this to some sort of HDD base class
        static void CheckOffsets(long offset, long size, long mySize)
        {
            if (offset < 0 || size <= 0 || offset + size > mySize)
                throw new ArgumentOutOfRangeException();
        }

        public static IHardDisk Create(IHardDisk hdd)
        {
            VhdHeader head = GetHeader(hdd);
            if (head.CookieStr != "conectix")
                throw new Exception();

            if (head.DiskType == DiskType.Fixed)
            {
                return new FixedVhd(hdd);
            }
            else if (head.DiskType == DiskType.Dynamic)
            {
                throw new NotImplementedException();
            }
            else
            {
                throw new Exception("Only fixed size VHDs are supported.");
            }
        }

        class FixedVhd : IHardDisk
        {
            long mSize;
            IHardDisk mHdd;

            public FixedVhd(IHardDisk hdd)
            {
                mHdd = hdd;
                mSize = hdd.Length - 512;
                var head = GetHeader(hdd);

                if (head.CurrentSize != mSize)
                    throw new Exception();
            }

            public void Get<T>(long offset, out T @struct) where T : struct
            {
                CheckOffsets(offset, Marshal.SizeOf(typeof(T)), mSize);
                mHdd.Get<T>(offset, out @struct);
            }

            public byte[] ReadBytes(long offset, long count)
            {
                CheckOffsets(offset, count, mSize);
                return mHdd.ReadBytes(offset, count);
            }

            public long Length
            {
                get { return mSize; }
            }
        }

        class DynamicVhd
        {
            public DynamicVhd(IHardDisk hdd)
            {
                VhdHeader head = GetHeader(hdd);
                int dySize = Marshal.SizeOf(typeof(DynamicHeader));
                DynamicHeader dyhead = Program.ToStructByteSwap<DynamicHeader>(hdd.ReadBytes(head.DataOffset, dySize));
                if (dyhead.CookieStr != "cxsparse")
                    throw new Exception();
            }
        }
    }
}
