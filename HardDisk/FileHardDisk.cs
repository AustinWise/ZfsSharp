using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace ZfsSharp.HardDisk
{
    class FileHardDisk : IHardDisk
    {
        private MemoryMappedFile mFile;
        private long mSize;

        public FileHardDisk(string path)
        {
            var fi = new FileInfo(path);
            mSize = fi.Length;

            mFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
        }

        public void Get<T>(long offset, out T @struct) where T : struct
        {
            using (var ac = mFile.CreateViewAccessor(offset, Marshal.SizeOf(typeof(T))))
            {
                ac.Read(0, out @struct);
            }
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
