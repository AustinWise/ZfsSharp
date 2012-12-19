using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp.HardDisk
{
    class OffsetHardDisk : IHardDisk
    {
        IHardDisk mHdd;
        long mOffset;
        long mSize;

        public OffsetHardDisk(IHardDisk hdd, long offset, long size)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException();
            if (offset + size > hdd.Length)
                throw new ArgumentOutOfRangeException();

            mHdd = hdd;
            mOffset = offset;
            mSize = size;
        }

        void checkOffsets(long offset, long size)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException();
            if (offset + size > mSize)
                throw new ArgumentOutOfRangeException();
        }

        public void Get<T>(long offset, out T @struct) where T : struct
        {
            checkOffsets(offset, Marshal.SizeOf(typeof(T)));
            mHdd.Get<T>(mOffset + offset, out @struct);
        }

        public Stream GetStream(long offset, long size)
        {
            checkOffsets(offset, size);
            return mHdd.GetStream(offset + mOffset, size);
        }

        public Stream GetStream()
        {
            return mHdd.GetStream(mOffset, mSize);
        }

        public long Length
        {
            get { return mSize; }
        }
    }
}
