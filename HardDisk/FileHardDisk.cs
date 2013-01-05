using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

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
            using (var ac = mFile.CreateViewAccessor(offset, Marshal.SizeOf(typeof(T))))
            {
                ac.Read(0, out @struct);
            }
        }

        public override void ReadBytes(byte[] array, long arrayOffset, long offset, long count)
        {
            CheckOffsets(offset, count);
            using (var acc = mFile.CreateViewAccessor(offset, count))
            {
                acc.ReadArray(arrayOffset, array, 0, (int)count);
            }
        }

        public override long Length
        {
            get { return mSize; }
        }
    }
}
