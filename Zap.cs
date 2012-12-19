using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    class Zap
    {
        readonly Dmu mDmu;

        public Zap(Dmu dmu)
        {
            mDmu = dmu;
        }

        unsafe public Dictionary<string, long> Parse(dnode_phys_t dn)
        {
            Dictionary<string, long> ret = new Dictionary<string, long>();

            var zapBytes = mDmu.Read(dn);
            fixed (byte* ptr = zapBytes)
            {
                mzap_phys_t zapHeader = (mzap_phys_t)Marshal.PtrToStructure(new IntPtr(ptr), typeof(mzap_phys_t));

                if (zapHeader.BlockType != ZapBlockType.MICRO)
                    throw new NotImplementedException();

                for (int i = sizeof(mzap_phys_t); i < zapBytes.Length; i += sizeof(mzap_ent_phys_t))
                {
                    mzap_ent_phys_t entry = (mzap_ent_phys_t)Marshal.PtrToStructure(new IntPtr(ptr + i), typeof(mzap_ent_phys_t));
                    if (string.IsNullOrEmpty(entry.Name))
                        break;
                    if (entry.CD != 0)
                        throw new NotImplementedException();
                    ret.Add(entry.Name, entry.Value);
                }
            }

            return ret;
        }

        enum ZapBlockType : long
        {
            LEAF = ((1L << 63) + 0),
            HEADER = ((1L << 63) + 1),
            MICRO = ((1L << 63) + 3),
        }

        const int MZAP_ENT_LEN = 64;
        const int MZAP_NAME_LEN = (MZAP_ENT_LEN - 8 - 4 - 2);
        const int MZAP_MAX_BLKSHIFT = Program.SPA_MAXBLOCKSHIFT;
        const long MZAP_MAX_BLKSZ = (1 << MZAP_MAX_BLKSHIFT);

        const ulong ZAP_MAGIC = 0x2F52AB2ABL;

        const long ZAP_NEED_CD = (-1U);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct mzap_ent_phys_t
        {
            public long Value;
            public uint CD;
            short pad;	/* in case we want to chain them someday */
            fixed byte name[MZAP_NAME_LEN];

            public string Name
            {
                get
                {
                    fixed (byte* ptr = name)
                    {
                        string ret = Marshal.PtrToStringAnsi(new IntPtr(ptr), MZAP_NAME_LEN);
                        int zeroNdx = ret.IndexOf('\0');
                        if (zeroNdx != -1)
                            ret = ret.Substring(0, zeroNdx);
                        return ret;
                    }
                }
            }

            public override string ToString()
            {
                return string.Format("{0}: {1}", Name, Value);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct mzap_phys_t
        {
            public ZapBlockType BlockType;	/* ZBT_MICRO */
            public ulong Salt;
            public ulong NormFlags;
            fixed ulong mz_pad[5];
            //fixed mzap_ent_phys_t mz_chunk[1];
            /* actually variable size depending on block size */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct zap_phys_t
        {
            public ZapBlockType zap_block_type;	/* ZBT_HEADER */
            public ulong zap_magic;		/* ZAP_MAGIC */

            zap_table_phys zap_ptrtbl;
            ulong zap_freeblk;		/* the next free block */
            ulong zap_num_leafs;		/* number of leafs */
            ulong zap_num_entries;	/* number of entries */
            ulong zap_salt;		/* salt to stir into hash function */
            ulong zap_normflags;		/* flags for u8_textprep_str() */
            ulong zap_flags;		/* zap_flags_t */
            /*
             * This structure is followed by padding, and then the embedded
             * pointer table.  The embedded pointer table takes up second
             * half of the block.  It is accessed using the
             * ZAP_EMBEDDED_PTRTBL_ENT() macro.
             */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct zap_table_phys
        {
            ulong zt_blk;	/* starting block number */
            ulong zt_numblks;	/* number of blocks */
            ulong zt_shift;	/* bits to index it */
            ulong zt_nextblk;	/* next (larger) copy start block */
            ulong zt_blks_copied; /* number source blocks copied */
        }
    }
}
