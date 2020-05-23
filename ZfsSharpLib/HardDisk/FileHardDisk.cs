using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace ZfsSharpLib.HardDisks
{
    unsafe class FileHardDisk : HardDisk
    {
        private MemoryMappedFile mFile;
        private MemoryMappedViewAccessor mViewAcessor;
        private byte* mPointer;
        private long mSize;

        public FileHardDisk(string path)
        {
            mFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
            mViewAcessor = mFile.CreateViewAccessor();
            long fileSize = new FileInfo(path).Length;
            //Limit the range of data we read to the Capacity of the ViewAccessor
            //in the unlikly case that it is smaller than the file size we read.
            //We can't just use the Capacity though, as it is round up to the page size.
            mSize = Math.Min(mViewAcessor.Capacity, fileSize);
            mPointer = null;
            mViewAcessor.SafeMemoryMappedViewHandle.AcquirePointer(ref mPointer);
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            new Span<byte>(mPointer + offset, dest.Length).CopyTo(dest);
        }

        public override long Length
        {
            get { return mSize; }
        }

        public override void Dispose()
        {
            mPointer = null;
            mViewAcessor.SafeMemoryMappedViewHandle.ReleasePointer();
            mViewAcessor.Dispose();
            mFile.Dispose();
        }
    }
}
