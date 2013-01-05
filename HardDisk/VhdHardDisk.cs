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
    class VhdHardDisk : IHardDisk
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
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct VhdHeader
        {
            public fixed byte Cookie[8];
            public Features Features;
            public int FileFormatVersion;
            public ulong DataOffset;
            public int TimeStamp;
            public fixed byte CreatorApp[4];
            public int CreatorVersion;
            public CreatorOs CreatorOs;
            public long OriginalSize;
            public long CurrentSize;
            public int DiskGeometry;
            public int DiskType;
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
        #endregion

        private MemoryMappedFile mFile;
        private long mSize;

        public void Get<T>(long offset, out T @struct) where T : struct
        {
            using (var ac = mFile.CreateViewAccessor(offset, Marshal.SizeOf(typeof(T))))
            {
                ac.Read(0, out @struct);
            }
        }

        public VhdHardDisk(string path)
        {
            var fi = new FileInfo(path);

            mFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open);

            byte[] headerBytes = new byte[Marshal.SizeOf(typeof(VhdHeader))];
            using (var ac = mFile.CreateViewAccessor(fi.Length - 512, 512))
            {
                ac.ReadArray(0, headerBytes, 0, headerBytes.Length);
            }
            VhdHeader head = Program.ToStructByteSwap<VhdHeader>(headerBytes);

            if (head.DataOffset != 0xffffffffffffffff)
            {
                mFile.Dispose();
                throw new Exception("Only fixed size VHDs are supported.");
            }

            mSize = head.CurrentSize;
        }

        public byte[] ReadBytes(long offset, long count)
        {
            if (offset < 0 || count <= 0 || offset + count > mSize)
                throw new ArgumentOutOfRangeException();
            if (count > Int32.MaxValue)
                throw new ArgumentOutOfRangeException();
            using (var acc = mFile.CreateViewAccessor(offset, count))
            {
                var ret = new byte[count];
                acc.ReadArray(0, ret, 0, (int)count);
                return ret;
            }
        }

        public long Length
        {
            get { return mSize; }
        }
    }
}
