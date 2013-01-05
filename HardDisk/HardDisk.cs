using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    abstract class HardDisk
    {
        protected void CheckOffsets(long offset, long size)
        {
            if (offset < 0 || size <= 0 || offset + size > Length)
                throw new ArgumentOutOfRangeException();
        }

        public abstract void Get<T>(long offset, out T @struct) where T : struct;

        public virtual byte[] ReadBytes(long offset, long count)
        {
            var ret = new byte[count];
            ReadBytes(ret, 0, offset, count);
            return ret;
        }

        public abstract void ReadBytes(byte[] array, long arrayOffset, long offset, long count);

        public abstract long Length
        {
            get;
        }
    }
}
