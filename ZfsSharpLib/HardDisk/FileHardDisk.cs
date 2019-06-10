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
            mSize = mViewAcessor.Capacity;
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
            mViewAcessor.SafeMemoryMappedViewHandle.ReleasePointer();
            mViewAcessor.Dispose();
            mFile.Dispose();
        }
    }
}
