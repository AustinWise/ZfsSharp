using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace ZfsSharp.HardDisks
{
    class FileHardDisk : HardDisk
    {
        private MemoryMappedFile mFile;
        private long mSize;

        public FileHardDisk(string path)
        {
            var fi = new FileInfo(path);
            mSize = fi.Length;

            mFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
        }

        public override void ReadBytes(ArraySegment<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Count);
            using (var s = mFile.CreateViewStream(offset, dest.Count, MemoryMappedFileAccess.Read))
            {
                var rc = s.Read(dest.Array, dest.Offset, dest.Count);
                if (rc != dest.Count)
                    throw new IOException("Not enough bytes read.");
            }
        }

        public override long Length
        {
            get { return mSize; }
        }

        public override void Dispose()
        {
            mFile.Dispose();
        }
    }
}
