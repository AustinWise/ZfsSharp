﻿using System.IO;
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

        public override void Get<T>(long offset, out T @struct)
        {
            int size = Program.SizeOf<T>();
            CheckOffsets(offset, size);
            using (var ac = mFile.CreateViewAccessor(offset, size))
            {
                ac.Read(0, out @struct);
            }
        }

        public override void ReadBytes(byte[] array, int arrayOffset, long offset, int count)
        {
            CheckOffsets(offset, count);
            using (var s = mFile.CreateViewStream(offset, count, MemoryMappedFileAccess.Read))
            {
                var rc = s.Read(array, (int)arrayOffset, (int)count);
                if (rc != count)
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
