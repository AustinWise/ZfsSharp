using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp.HardDisks
{
    //TODO: consider a static creation method that sees if the harddrive it is wrapping is a OffsetHardDrive.
    //In that case, it would would wrap the underlying hard disk instead of creating another indirection layer.
    class OffsetHardDisk : HardDisk
    {
        HardDisk mHdd;
        long mOffset;
        long mSize;

        public OffsetHardDisk(HardDisk hdd, long offset, long size)
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

        public override void Get<T>(long offset, out T @struct)
        {
            CheckOffsets(offset, Marshal.SizeOf(typeof(T)));
            mHdd.Get<T>(mOffset + offset, out @struct);
        }

        public override void ReadBytes(byte[] array, long arrayOffset, long offset, long count)
        {
            CheckOffsets(offset, count);
            mHdd.ReadBytes(array, arrayOffset, mOffset + offset, count);
        }

        public override byte[] ReadBytes(long offset, long count)
        {
            CheckOffsets(offset, count);
            return mHdd.ReadBytes(mOffset + offset, count);
        }

        public override long Length
        {
            get { return mSize; }
        }
    }
}
