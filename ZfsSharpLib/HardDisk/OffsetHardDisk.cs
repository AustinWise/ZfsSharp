using System;

namespace ZfsSharpLib.HardDisks
{
    class OffsetHardDisk : HardDisk
    {
        HardDisk mHdd;
        long mOffset;
        long mSize;

        public static HardDisk Create(HardDisk hdd, long offset, long size)
        {
            while (hdd is OffsetHardDisk)
            {
                var off = (OffsetHardDisk)hdd;
                offset += off.mOffset;
                hdd = off.mHdd;
            }
            return new OffsetHardDisk(hdd, offset, size);
        }

        private OffsetHardDisk(HardDisk hdd, long offset, long size)
        {
            Init(hdd, offset, size);
        }

        protected OffsetHardDisk()
        {
        }

        protected void Init(HardDisk hdd, long offset, long size)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException();
            if (offset + size > hdd.Length)
                throw new ArgumentOutOfRangeException();

            mHdd = hdd;
            mOffset = offset;
            mSize = size;
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            mHdd.ReadBytes(dest, mOffset + offset);
        }

        public override long Length
        {
            get { return mSize; }
        }

        public override void Dispose()
        {
            mHdd.Dispose();
        }
    }
}
