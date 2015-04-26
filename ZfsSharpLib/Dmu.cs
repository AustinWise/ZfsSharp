﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    class Dmu
    {
        private Zio mZio;
        public Dmu(Zio zio)
        {
            mZio = zio;
        }

        unsafe static void CalculateBonusSize(ref dnode_phys_t dn, out int bonusOffset, out int maxBonusSize)
        {
            if (dn.BonusType == dmu_object_type_t.NONE)
                throw new Exception("No bonus type.");

            bonusOffset = (dn.NBlkPtrs - 1) * sizeof(blkptr_t);
            maxBonusSize = dnode_phys_t.DN_MAX_BONUSLEN - bonusOffset;
            if ((dn.Flags & DnodeFlags.SpillBlkptr) != 0)
            {
                maxBonusSize -= sizeof(blkptr_t);
            }
        }

        unsafe public T GetBonus<T>(dnode_phys_t dn) where T : struct
        {
            Type t = typeof(T);
            int structSize = Marshal.SizeOf(t);
            int bonusOffset;
            int maxBonusSize;
            CalculateBonusSize(ref dn, out bonusOffset, out maxBonusSize);

            if (structSize > maxBonusSize)
                throw new ArgumentOutOfRangeException();
            if (structSize > dn.BonusLen)
                throw new ArgumentOutOfRangeException();

            return (T)Marshal.PtrToStructure(new IntPtr(dn.Bonus + bonusOffset), typeof(T));
        }

        public byte[] ReadSpill(dnode_phys_t dn)
        {
            if ((dn.Flags & DnodeFlags.SpillBlkptr) == 0)
            {
                throw new NotSupportedException("DNode does not have a spill block pointer.");
            }

            var spill = dn.Spill;
            if (spill.fill != 1)
            {
                throw new NotImplementedException("Only spill pointers with fill = 1 supported.");
            }

            return mZio.Read(spill);
        }

        unsafe public byte[] ReadBonus(dnode_phys_t dn)
        {
            int bonusOffset;
            int maxBonusSize;
            CalculateBonusSize(ref dn, out bonusOffset, out maxBonusSize);

            if (dn.BonusLen > maxBonusSize)
                throw new Exception("Specified bonus size is larger than the dnode can hold.");

            byte[] bonus = new byte[dn.BonusLen];
            Marshal.Copy(new IntPtr(dn.Bonus + bonusOffset), bonus, 0, dn.BonusLen);
            return bonus;
        }

        unsafe public dnode_phys_t ReadFromObjectSet(objset_phys_t os, long index)
        {
            var dnStuff = Read(os.MetaDnode, index << dnode_phys_t.DNODE_SHIFT, sizeof(dnode_phys_t));
            return Program.ToStruct<dnode_phys_t>(dnStuff);
        }

        public byte[] Read(dnode_phys_t dn, long offset, long size)
        {
            if (offset < 0 || size < 0)
                throw new ArgumentOutOfRangeException();
            long blockSize = dn.DataBlkSizeSec * 512;
            long maxSize = (dn.MaxBlkId + 1) * blockSize;
            if ((offset + size) > maxSize)
                throw new ArgumentOutOfRangeException();

            var ret = new byte[size];
            Program.MultiBlockCopy<blkptr_t>(ret, 0, offset, size, blockSize, blkId => GetBlock(ref dn, blkId), readBlock);
            return ret;
        }

        public void Read(dnode_phys_t dn, byte[] buffer, long offset, long size)
        {
            if (offset < 0 || size < 0)
                throw new ArgumentOutOfRangeException();
            long blockSize = dn.DataBlkSizeSec * 512;
            long maxSize = (dn.MaxBlkId + 1) * blockSize;
            if ((offset + size) > maxSize)
                throw new ArgumentOutOfRangeException();

            Program.MultiBlockCopy<blkptr_t>(buffer, 0, offset, size, blockSize, blkId => GetBlock(ref dn, blkId), readBlock);
        }

        private void readBlock(blkptr_t blkptr, byte[] dest, long destOffset, long startNdx, long cpyCount)
        {
            var src = mZio.Read(blkptr);
            Program.LongBlockCopy(src, startNdx, dest, destOffset, cpyCount);
        }

        private blkptr_t GetBlock(ref dnode_phys_t dn, long blockId)
        {
            int indirBlockShift = dn.IndirectBlockShift - blkptr_t.SPA_BLKPTRSHIFT;
            long indirMask = (1 << indirBlockShift) - 1;

            var indirOffsets = new Stack<long>(dn.NLevels);
            for (int i = 0; i < dn.NLevels; i++)
            {
                indirOffsets.Push(blockId & indirMask);
                blockId >>= indirBlockShift;
            }

            blkptr_t ptr = dn.GetBlkptr(indirOffsets.Pop());
            while (indirOffsets.Count != 0)
            {
                var indirBlock = mZio.Read(ptr);
                var indirectNdx = indirOffsets.Pop();
                ptr = Program.ToStruct<blkptr_t>(indirBlock, indirectNdx * (1 << blkptr_t.SPA_BLKPTRSHIFT));
            }
            return ptr;
        }

        public byte[] Read(dnode_phys_t dn)
        {
            return Read(dn, 0, (dn.MaxBlkId + 1) * (dn.DataBlkSizeSec * 512));
        }
    }
}