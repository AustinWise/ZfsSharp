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

        /// <summary>
        /// Reads and verifies data from a label.
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns>Null if the checksum is not valid.</returns>
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
                return null;
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

        public void ReadBytes(ArraySegment<byte> dest, long offset)
        {
            ReadBytes(dest.Array, dest.Offset, offset, dest.Count);
        }

        public abstract long Length
        {
            get;
        }

        public abstract void Dispose();
    }
}
