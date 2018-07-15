using System;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    class DNode
    {
        readonly Zio mZio;
        readonly Func<long, blkptr_t> mGetBlockKey;
        readonly Program.BlockReader<blkptr_t> mReadBlock;
        dnode_phys_t mPhys;

        public DNode(Zio zio, dnode_phys_t phys)
        {
            if (phys.NLevels == 0)
                throw new ArgumentOutOfRangeException(nameof(phys), "Expect dnode's NLevels to be > 0");

            mZio = zio;
            mGetBlockKey = getBlockKey;
            mReadBlock = readBlock;
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

        public int SpillSize
        {
            get
            {
                if ((mPhys.Flags & DnodeFlags.SpillBlkptr) == 0)
                {
                    throw new NotSupportedException("DNode does not have a spill block pointer.");
                }
                return mPhys.Spill.LogicalSizeBytes;
            }
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

        public void ReadSpill(Span<byte> dest)
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

            mZio.Read(spill, dest);
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns>A buffer that should be return to <see cref="Program.ReturnBytes"/></returns>
        unsafe public ArraySegment<byte> RentBonus()
        {
            int bonusOffset;
            int maxBonusSize;
            CalculateBonusSize(out bonusOffset, out maxBonusSize);

            if (mPhys.BonusLen > maxBonusSize)
                throw new Exception("Specified bonus size is larger than the dnode can hold.");

            var bonus = Program.RentBytes(mPhys.BonusLen);
            fixed (byte* pBonus = mPhys.Bonus)
            {
                Marshal.Copy(new IntPtr(pBonus + bonusOffset), bonus.Array, bonus.Offset, bonus.Count);
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
            Read(new Span<byte>(buffer, 0, size), offset);
        }

        public void Read(ArraySegment<byte> dest, long offset)
        {
            Read((Span<byte>)dest, offset);
        }

        public void Read(Span<byte> dest, long offset)
        {
            if (offset < 0 || dest.Length < 0)
                throw new ArgumentOutOfRangeException();
            if ((offset + dest.Length) > mPhys.AvailableDataSize)
                throw new ArgumentOutOfRangeException();

            Program.MultiBlockCopy<blkptr_t>(dest, offset, mPhys.BlockSizeInBytes, mGetBlockKey, mReadBlock);
        }

        private void readBlock(Span<byte> dest, blkptr_t blkptr, int startNdx)
        {
            if (blkptr.IsHole)
                return;
            int logicalBlockSize = blkptr.LogicalSizeBytes;
            if (logicalBlockSize == dest.Length)
            {
                mZio.Read(blkptr, dest);
            }
            else
            {
                var src = Program.RentBytes(logicalBlockSize);
                mZio.Read(blkptr, src);
                new Span<byte>(src.Array, src.Offset + startNdx, dest.Length).CopyTo(dest);
                Program.ReturnBytes(src);
            }
        }

        private blkptr_t getBlockKey(long blockId)
        {
            int indirBlockShift = mPhys.IndirectBlockShift - blkptr_t.SPA_BLKPTRSHIFT;
            int indirMask = (1 << indirBlockShift) - 1;
            int indirSize = 1 << mPhys.IndirectBlockShift;

            blkptr_t ptr = default(blkptr_t);

            for (int i = 0; i < mPhys.NLevels; i++)
            {
                int indirectNdx = (int)(blockId >> ((mPhys.NLevels - i - 1) * indirBlockShift)) & indirMask;
                if (i == 0)
                {
                    ptr = mPhys.GetBlkptr(indirectNdx);
                }
                else
                {
                    var indirBlockRent = Program.RentBytes(indirSize);
                    Span<byte> indirBlock = indirBlockRent;
                    mZio.Read(ptr, indirBlock);
                    const int BP_SIZE = 1 << blkptr_t.SPA_BLKPTRSHIFT;
                    ptr = Program.ToStruct<blkptr_t>(indirBlock.Slice(indirectNdx * BP_SIZE, BP_SIZE));
                    Program.ReturnBytes(indirBlockRent);
                }

                if (ptr.IsHole)
                    break;
            }

            return ptr;
        }
    }
}
