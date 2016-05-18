using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    class DNode
    {
        static readonly ArrayPool<byte> sPool = ArrayPool<byte>.Shared;

        readonly Zio mZio;
        dnode_phys_t mPhys;

        public DNode(Zio zio, dnode_phys_t phys)
        {
            mZio = zio;
            mPhys = phys;
        }

        public dmu_object_type_t Type
        {
            get { return mPhys.Type; }
        }

        public dmu_object_type_t BonusType
        {
            get { return mPhys.BonusType; }
        }

        public bool IsNewType
        {
            get { return mPhys.IsNewType; }
        }

        public dmu_object_byteswap NewType
        {
            get { return mPhys.NewType; }
        }

        public int BlockSizeInBytes
        {
            get { return mPhys.BlockSizeInBytes; }
        }

        /// <summary>
        /// Maximum amount of data that can be read from this DNode.
        /// </summary>
        public long AvailableDataSize
        {
            get { return mPhys.AvailableDataSize; }
        }

        public DnodeFlags Flags
        {
            get { return mPhys.Flags; }
        }

        public dmu_object_type_t SpillType
        {
            get { return (mPhys.Flags & DnodeFlags.SpillBlkptr) == 0 ? dmu_object_type_t.NONE : mPhys.Spill.Type; }
        }

        unsafe void CalculateBonusSize(out int bonusOffset, out int maxBonusSize)
        {
            if (mPhys.BonusType == dmu_object_type_t.NONE)
                throw new Exception("No bonus type.");

            bonusOffset = (mPhys.NBlkPtrs - 1) * sizeof(blkptr_t);
            maxBonusSize = dnode_phys_t.DN_MAX_BONUSLEN - bonusOffset;
            if ((mPhys.Flags & DnodeFlags.SpillBlkptr) != 0)
            {
                maxBonusSize -= sizeof(blkptr_t);
            }
        }

        unsafe public T GetBonus<T>() where T : struct
        {
            Type t = typeof(T);
            int structSize = Program.SizeOf<T>();
            int bonusOffset;
            int maxBonusSize;
            CalculateBonusSize(out bonusOffset, out maxBonusSize);

            if (structSize > maxBonusSize)
                throw new ArgumentOutOfRangeException();
            if (structSize > mPhys.BonusLen)
                throw new ArgumentOutOfRangeException();

            fixed (byte* pBonus = mPhys.Bonus)
            {
                return Program.ToStruct<T>(pBonus, bonusOffset, maxBonusSize);
            }
        }

        public byte[] ReadSpill()
        {
            if ((mPhys.Flags & DnodeFlags.SpillBlkptr) == 0)
            {
                throw new NotSupportedException("DNode does not have a spill block pointer.");
            }

            var spill = mPhys.Spill;
            if (spill.fill != 1)
            {
                throw new NotImplementedException("Only spill pointers with fill = 1 supported.");
            }

            var ret = new byte[spill.LogicalSizeBytes];
            mZio.Read(spill, new ArraySegment<byte>(ret));
            return ret;
        }

        unsafe public byte[] ReadBonus()
        {
            int bonusOffset;
            int maxBonusSize;
            CalculateBonusSize(out bonusOffset, out maxBonusSize);

            if (mPhys.BonusLen > maxBonusSize)
                throw new Exception("Specified bonus size is larger than the dnode can hold.");

            byte[] bonus = new byte[mPhys.BonusLen];
            fixed (byte* pBonus = mPhys.Bonus)
            {
                Marshal.Copy(new IntPtr(pBonus + bonusOffset), bonus, 0, mPhys.BonusLen);
            }
            return bonus;
        }

        public byte[] Read(long offset, int size)
        {
            var ret = new byte[size];
            Read(ret, offset, size);
            return ret;
        }

        public void Read(byte[] buffer, long offset, int size)
        {
            if (offset < 0 || size < 0)
                throw new ArgumentOutOfRangeException();
            if ((offset + size) > mPhys.AvailableDataSize)
                throw new ArgumentOutOfRangeException();

            Program.MultiBlockCopy<blkptr_t>(new ArraySegment<byte>(buffer, 0, size), offset, mPhys.BlockSizeInBytes, GetBlock, readBlock);
        }

        private void readBlock(ArraySegment<byte> dest, blkptr_t blkptr, int startNdx)
        {
            if (blkptr.IsHole)
                return;
            int logicalBlockSize = blkptr.LogicalSizeBytes;
            if (logicalBlockSize == dest.Count)
            {
                mZio.Read(blkptr, dest);
            }
            else
            {
                var src = sPool.Rent(logicalBlockSize);
                mZio.Read(blkptr, new ArraySegment<byte>(src, 0, logicalBlockSize));
                Buffer.BlockCopy(src, startNdx, dest.Array, dest.Offset, dest.Count);
                sPool.Return(src);
            }
        }

        private blkptr_t GetBlock(long blockId)
        {
            int indirBlockShift = mPhys.IndirectBlockShift - blkptr_t.SPA_BLKPTRSHIFT;
            int indirMask = (1 << indirBlockShift) - 1;

            var indirOffsets = new Stack<int>(mPhys.NLevels);
            for (int i = 0; i < mPhys.NLevels; i++)
            {
                indirOffsets.Push((int)(blockId & indirMask));
                blockId >>= indirBlockShift;
            }

            blkptr_t ptr = mPhys.GetBlkptr(indirOffsets.Pop());

            if (indirOffsets.Count != 0)
            {
                int indirSize = 1 << mPhys.IndirectBlockShift;
                var indirBlock = new ArraySegment<byte>(sPool.Rent(indirSize), 0, indirSize);
                while (indirOffsets.Count != 0 && !ptr.IsHole)
                {
                    mZio.Read(ptr, indirBlock);
                    var indirectNdx = indirOffsets.Pop();
                    const int BP_SIZE = 1 << blkptr_t.SPA_BLKPTRSHIFT;
                    ptr = Program.ToStruct<blkptr_t>(indirBlock.SubSegment(indirectNdx * BP_SIZE, BP_SIZE));
                }
                sPool.Return(indirBlock.Array);
            }

            return ptr;
        }

        public byte[] Read()
        {
            return Read(0, (int)mPhys.AvailableDataSize);
        }
    }
}
