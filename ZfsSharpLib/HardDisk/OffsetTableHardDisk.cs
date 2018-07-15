using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZfsSharp
{
    abstract class OffsetTableHardDisk : HardDisk
    {
        protected readonly HardDisk mHdd;
        readonly Program.BlockReader<long> mReadBlock;
        readonly Func<long, long> mGetBlockKey;

        //need to be set by subclass
        protected long[] mBlockOffsets;
        protected int mBlockSize;

        protected OffsetTableHardDisk(HardDisk hdd)
        {
            if (hdd == null)
                throw new ArgumentNullException(nameof(hdd));
            mHdd = hdd;
            mReadBlock = readBlock;
            mGetBlockKey = getBlockKey;
        }

        public override long Length
        {
            get
            {
                return mBlockSize * mBlockOffsets.LongLength;
            }
        }

        public override void Dispose()
        {
            mHdd.Dispose();
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            Program.MultiBlockCopy<long>(dest, offset, mBlockSize, mGetBlockKey, mReadBlock);
        }

        long getBlockKey(long blockId)
        {
            return mBlockOffsets[blockId];
        }

        void readBlock(Span<byte> array, long blockOffset, int blockStartNdx)
        {
            if (blockOffset == -1)
            {
                array.Fill(0);
            }
            else
            {
                mHdd.ReadBytes(array, blockOffset + blockStartNdx);
            }
        }
    }
}
