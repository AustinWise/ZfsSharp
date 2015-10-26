using System;

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

        public unsafe byte[] ReadLabelBytes(long offset, int count)
        {
            var ret = ReadBytes(offset, count);
            var verifier = new zio_cksum_t()
            {
                word1 = (ulong)offset,
                word2 = 0,
                word3 = 0,
                word4 = 0,
            };
            if (!Zio.IsEmbeddedChecksumValid(ret, verifier))
            {
                throw new Exception("Invalided label checksum.");
            }

            return ret;
        }

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
