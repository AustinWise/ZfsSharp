using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    abstract class HardDisk : IDisposable
    {
        protected void CheckOffsets(long offset, long size)
        {
            if (offset < 0 || size <= 0 || offset + size > Length)
                throw new ArgumentOutOfRangeException();
        }

        public abstract void Get<T>(long offset, out T @struct) where T : struct;

        public virtual byte[] ReadBytes(long offset, int count)
        {
            var ret = new byte[count];
            ReadBytes(ret, 0, offset, count);
            return ret;
        }

        public abstract void ReadBytes(byte[] array, int arrayOffset, long offset, int count);

        public abstract long Length
        {
            get;
        }

        public abstract void Dispose();
    }
}
