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

        public void Get<T>(long offset, out T @struct) where T : struct
        {
            int structSize = Program.SizeOf<T>();
            CheckOffsets(offset, structSize);
            var bytes = Program.RentBytes(structSize);
            ReadBytes(bytes, offset);
            @struct = Program.ToStruct<T>(bytes);
            Program.ReturnBytes(bytes);
        }

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

        public byte[] ReadBytes(long offset, int count)
        {
            var ret = new byte[count];
            ReadBytes(new ArraySegment<byte>(ret, 0, count), offset);
            return ret;
        }

        public abstract void ReadBytes(ArraySegment<byte> dest, long offset);

        public abstract long Length
        {
            get;
        }

        public abstract void Dispose();
    }
}
