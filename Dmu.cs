using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp
{
    class Dmu
    {
        private Zio mZio;
        public Dmu(Zio zio)
        {
            mZio = zio;
        }

        unsafe public T GetBonus<T>(dnode_phys_t dn) where T : struct
        {
            Type t = typeof(T);
            int bonusOffset = (dn.NBlkPtrs - 1) * sizeof(blkptr_t);
            int bonusSize = dnode_phys_t.DN_MAX_BONUSLEN - bonusOffset;

            if (Marshal.SizeOf(t) > bonusSize)
                throw new ArgumentOutOfRangeException();

            return (T)Marshal.PtrToStructure(new IntPtr(dn.Bonus + bonusOffset), typeof(T));
        }

        unsafe public byte[] ReadBonus(dnode_phys_t dn)
        {
            int bonusOffset = (dn.NBlkPtrs - 1) * sizeof(blkptr_t);
            int bonusSize = dnode_phys_t.DN_MAX_BONUSLEN - bonusOffset;

            byte[] bonus = new byte[bonusSize];
            Marshal.Copy(new IntPtr(dn.Bonus + bonusOffset), bonus, 0, bonusSize);
            return bonus;
        }

        unsafe public dnode_phys_t ReadFromObjectSet(objset_phys_t os, long index)
        {
            var dnStuff = Read(os.MetaDnode, index << dnode_phys_t.DNODE_SHIFT, sizeof(dnode_phys_t));
            return Program.ToStruct<dnode_phys_t>(dnStuff);
        }

        //TODO: fix the off-by-one errors that are problably in here
        public byte[] Read(dnode_phys_t dn, long offset, long size)
        {
            if (offset < 0 || size <= 0)
                throw new ArgumentOutOfRangeException();
            long blockSize = dn.DataBlkSizeSec * 512;
            long maxSize = (dn.MaxBlkId + 1) * blockSize;
            if ((offset + size) > maxSize)
                throw new ArgumentOutOfRangeException();

            List<blkptr_t> dataBlockPtrs = new List<blkptr_t>();
            for (long i = offset; i < (offset + size); i += blockSize)
            {
                long blockId = i / blockSize;
                dataBlockPtrs.Add(GetBlock(ref dn, blockId));
            }

            var ret = new byte[size];
            long retNdx = 0;
            for (int i = 0; i < dataBlockPtrs.Count; i++)
            {
                int startNdx = 0;
                int cpyCount = (int)blockSize;
                if (i == 0)
                    startNdx += (int)(offset % blockSize);
                if (i == dataBlockPtrs.Count - 1)
                {
                    cpyCount = (int)((offset + size) % blockSize);
                    if (cpyCount == 0)
                        cpyCount = (int)blockSize;
                    cpyCount -= startNdx;
                }

                if (retNdx > Int32.MaxValue)
                    throw new NotImplementedException("No support for blocks this big yet.");

                var bytes = mZio.Read(dataBlockPtrs[i]);
                Buffer.BlockCopy(bytes, startNdx, ret, (int)retNdx, cpyCount);
                retNdx += blockSize;
            }

            //var dataBytes = mZio.Read(ptr);
            //var ret = new byte[size];
            //Buffer.BlockCopy(dataBytes, (int)(offset % blockSize), ret, 0, (int)size);
            return ret;
        }

        private blkptr_t GetBlock(ref dnode_phys_t dn, long blockId)
        {
            long indirMask = (1 << dn.IndirectBlockShift) - 1;

            var indirOffsets = new Stack<long>(dn.NLevels);
            for (int i = 0; i < dn.NLevels; i++)
            {
                indirOffsets.Push(blockId & indirMask);
                blockId >>= dn.IndirectBlockShift;
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct objset_phys_t
    {
        public const int OBJSET_PHYS_SIZE = 2048;

        public dnode_phys_t MetaDnode;
        public zil_header_t ZilHeader;
        public dmu_objset_type_t Type;
        public ulong Flags;
        fixed byte os_pad[OBJSET_PHYS_SIZE - (1 << dnode_phys_t.DNODE_SHIFT) * 3 -
            zil_header_t.SIZE - sizeof(ulong) * 2];
        public dnode_phys_t UserUsedDnode;
        public dnode_phys_t GroupUsedDnode;
    }

    enum dmu_objset_type_t : long
    {
        DMU_OST_NONE,
        DMU_OST_META,
        DMU_OST_ZFS,
        DMU_OST_ZVOL,
        DMU_OST_OTHER,			/* For testing only! */
        DMU_OST_ANY,			/* Be careful! */
        DMU_OST_NUMTYPES
    }

    [Flags]
    enum zh_header_flags : long
    {
        REPLAY_NEEDED = 0x1,	/* replay needed - internal only */
        CLAIM_LR_SEQ_VALID = 0x2,	/* zh_claim_lr_seq field is valid */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct zil_header_t
    {
        public const int SIZE = 0xc0;

        public ulong zh_claim_txg;	/* txg in which log blocks were claimed */
        public ulong zh_replay_seq;	/* highest replayed sequence number */
        public blkptr_t zh_log;	/* log chain */
        public ulong zh_claim_blk_seq; /* highest claimed block sequence number */
        public ulong zh_flags;	/* header flags */
        public ulong zh_claim_lr_seq; /* highest claimed lr sequence number */
        fixed ulong zh_pad[3];
    }

    enum dmu_object_type_t : byte
    {
        NONE,
        /* general: */
        OBJECT_DIRECTORY,	/* ZAP */
        OBJECT_ARRAY,		/* UINT64 */
        PACKED_NVLIST,		/* UINT8 (XDR by nvlist_pack/unpack) */
        PACKED_NVLIST_SIZE,	/* UINT64 */
        BPOBJ,			/* UINT64 */
        BPOBJ_HDR,		/* UINT64 */
        /* spa: */
        SPACE_MAP_HEADER,	/* UINT64 */
        SPACE_MAP,		/* UINT64 */
        /* zil: */
        INTENT_LOG,		/* UINT64 */
        /* dmu: */
        DNODE,			/* DNODE */
        OBJSET,			/* OBJSET */
        /* dsl: */
        DSL_DIR,			/* UINT64 */
        DSL_DIR_CHILD_MAP,	/* ZAP */
        DSL_DS_SNAP_MAP,		/* ZAP */
        DSL_PROPS,		/* ZAP */
        DSL_DATASET,		/* UINT64 */
        /* zpl: */
        ZNODE,			/* ZNODE */
        OLDACL,			/* Old ACL */
        PLAIN_FILE_CONTENTS,	/* UINT8 */
        DIRECTORY_CONTENTS,	/* ZAP */
        MASTER_NODE,		/* ZAP */
        UNLINKED_SET,		/* ZAP */
        /* zvol: */
        ZVOL,			/* UINT8 */
        ZVOL_PROP,		/* ZAP */
        /* other; for testing only! */
        PLAIN_OTHER,		/* UINT8 */
        UINT64_OTHER,		/* UINT64 */
        ZAP_OTHER,		/* ZAP */
        /* new object types: */
        ERROR_LOG,		/* ZAP */
        SPA_HISTORY,		/* UINT8 */
        SPA_HISTORY_OFFSETS,	/* spa_his_phys_t */
        POOL_PROPS,		/* ZAP */
        DSL_PERMS,		/* ZAP */
        ACL,			/* ACL */
        SYSACL,			/* SYSACL */
        FUID,			/* FUID table (Packed NVLIST UINT8) */
        FUID_SIZE,		/* FUID table size UINT64 */
        NEXT_CLONES,		/* ZAP */
        SCAN_QUEUE,		/* ZAP */
        USERGROUP_USED,		/* ZAP */
        USERGROUP_QUOTA,		/* ZAP */
        USERREFS,		/* ZAP */
        DDT_ZAP,			/* ZAP */
        DDT_STATS,		/* ZAP */
        SA,			/* System attr */
        SA_MASTER_NODE,		/* ZAP */
        SA_ATTR_REGISTRATION,	/* ZAP */
        SA_ATTR_LAYOUTS,		/* ZAP */
        SCAN_XLATE,		/* ZAP */
        DEDUP,			/* fake dedup BP from ddt_bp_create() */
        DEADLIST,		/* ZAP */
        DEADLIST_HDR,		/* UINT64 */
        DSL_CLONES,		/* ZAP */
        BPOBJ_SUBOBJ,		/* UINT64 */
        /*
         * Do not allocate new object types here. Doing so makes the on-disk
         * format incompatible with any other format that uses the same object
         * type number.
         *
         * When creating an object which does not have one of the above types
         * use the DMU_OTN_* type with the correct byteswap and metadata
         * values.
         *
         * The DMU_OTN_* types do not have entries in the dmu_ot table,
         * use the DMU_OT_IS_METDATA() and DMU_OT_BYTESWAP() macros instead
         * of indexing into dmu_ot directly (this works for both DMU_OT_* types
         * and DMU_OTN_* types).
         */
        DMU_OT_NUMTYPES,

        /*
         * Names for valid types declared with DMU_OT().
         */
        //DMU_OTN_UINT8_DATA = DMU_OT(DMU_BSWAP_UINT8, B_FALSE),
        //DMU_OTN_UINT8_METADATA = DMU_OT(DMU_BSWAP_UINT8, B_TRUE),
        //DMU_OTN_UINT16_DATA = DMU_OT(DMU_BSWAP_UINT16, B_FALSE),
        //DMU_OTN_UINT16_METADATA = DMU_OT(DMU_BSWAP_UINT16, B_TRUE),
        //DMU_OTN_UINT32_DATA = DMU_OT(DMU_BSWAP_UINT32, B_FALSE),
        //DMU_OTN_UINT32_METADATA = DMU_OT(DMU_BSWAP_UINT32, B_TRUE),
        //DMU_OTN_UINT64_DATA = DMU_OT(DMU_BSWAP_UINT64, B_FALSE),
        //DMU_OTN_UINT64_METADATA = DMU_OT(DMU_BSWAP_UINT64, B_TRUE),
        //DMU_OTN_ZAP_DATA = DMU_OT(DMU_BSWAP_ZAP, B_FALSE),
        //DMU_OTN_ZAP_METADATA = DMU_OT(DMU_BSWAP_ZAP, B_TRUE),
    }

    [Flags]
    enum DnodeFlags : byte
    {
        None = 0,
        UsedBytes = 1,
        SpillBlkptr = 2,
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    unsafe struct dnode_phys_t
    {

        /*
         * Fixed constants.
         */
        public const int DNODE_SHIFT = 9;	/* 512 bytes */
        const int DN_MIN_INDBLKSHIFT = 10;	/* 1k */
        const int DN_MAX_INDBLKSHIFT = 14;	/* 16k */
        const int DNODE_BLOCK_SHIFT = 14;	/* 16k */
        const int DNODE_CORE_SIZE = 64;	/* 64 bytes for dnode sans blkptrs */
        const int DN_MAX_OBJECT_SHIFT = 48;	/* 256 trillion (zfs_fid_t limit) */
        const int DN_MAX_OFFSET_SHIFT = 64;	/* 2^64 bytes in a dnode */

        /*
         * Derived constants.
         */
        const int DNODE_SIZE = (1 << DNODE_SHIFT);
        const int DN_MAX_NBLKPTR = ((DNODE_SIZE - DNODE_CORE_SIZE) >> blkptr_t.SPA_BLKPTRSHIFT);
        public const int DN_MAX_BONUSLEN = (DNODE_SIZE - DNODE_CORE_SIZE - (1 << blkptr_t.SPA_BLKPTRSHIFT));
        const long DN_MAX_OBJECT = (1L << DN_MAX_OBJECT_SHIFT);
        public const int DN_ZERO_BONUSLEN = (DN_MAX_BONUSLEN + 1);
        const int DN_KILL_SPILLBLK = (1);

        /*
         * Fields
         */
        [FieldOffset(0)]
        public dmu_object_type_t Type;
        [FieldOffset(1)]
        public byte IndirectBlockShift;
        [FieldOffset(2)]
        public byte NLevels;
        [FieldOffset(3)]
        public byte NBlkPtrs;
        [FieldOffset(4)]
        public dmu_object_type_t BonusType;
        [FieldOffset(5)]
        public zio_checksum Checksum;
        [FieldOffset(6)]
        public zio_compress Compress;
        [FieldOffset(7)]
        public DnodeFlags Flags;
        [FieldOffset(8)]
        public short DataBlkSizeSec;
        [FieldOffset(9)]
        public short BonusLen;

        [FieldOffset(0x10)]
        public long MaxBlkId;
        [FieldOffset(0x18)]
        public UInt64 Used;

        [FieldOffset(0x40)]
        public blkptr_t blkptr1;
        [FieldOffset(0x40 + 128)]
        public blkptr_t blkptr2;
        [FieldOffset(0x40 + 256)]
        public blkptr_t blkptr3;

        [FieldOffset(0xc0)]
        public fixed byte Bonus[DN_MAX_BONUSLEN];
        [FieldOffset(0x180)]
        public blkptr_t Spill;

        public blkptr_t GetBlkptr(long ndx)
        {
            if (ndx >= NBlkPtrs)
                throw new ArgumentOutOfRangeException();
            switch (ndx)
            {
                case 0:
                    return blkptr1;
                case 1:
                    return blkptr2;
                case 2:
                    return blkptr3;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

}