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
        /// <returns>true if the checksum is valid, false otherwise</returns>
        public bool ReadLabelBytes(ArraySegment<byte> dest, long offset)
        {
            ReadBytes(dest, offset);
            var verifier = new zio_cksum_t()
            {
                word1 = (ulong)offset,
                word2 = 0,
                word3 = 0,
                word4 = 0,
            };
            return Zio.IsEmbeddedChecksumValid(dest, verifier);
        }

        public byte[] ReadBytes(long offset, int count)
        {
            var ret = new byte[count];
            ReadBytes(new Span<byte>(ret), offset);
            return ret;
        }

        public abstract void ReadBytes(Span<byte> dest, long offset);

        public abstract long Length
        {
            get;
        }

        public abstract void Dispose();
    }
}
