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
            var zapBytes = mDmu.Read(dn);
            fixed (byte* ptr = zapBytes)
            {
                mzap_phys_t zapHeader = (mzap_phys_t)Marshal.PtrToStructure(new IntPtr(ptr), typeof(mzap_phys_t));

                if (zapHeader.BlockType == ZapBlockType.MICRO)
                    return ParseMicro(ptr, zapBytes.Length);
                else if (zapHeader.BlockType == ZapBlockType.HEADER)
                    return ParseFat(dn, ptr, zapBytes.Length);
                else
                    throw new NotSupportedException();
            }
        }

        unsafe Dictionary<string, long> ParseMicro(byte* ptr, int length)
        {
            Dictionary<string, long> ret = new Dictionary<string, long>();
            for (int i = sizeof(mzap_phys_t); i < length; i += sizeof(mzap_ent_phys_t))
            {
                mzap_ent_phys_t entry = (mzap_ent_phys_t)Marshal.PtrToStructure(new IntPtr(ptr + i), typeof(mzap_ent_phys_t));
                if (string.IsNullOrEmpty(entry.Name))
                    break;
                if (entry.CD != 0)
                    throw new NotImplementedException();
                ret.Add(entry.Name, entry.Value);
            }
            return ret;
        }

        unsafe Dictionary<string, long> ParseFat(dnode_phys_t dn, byte* ptr, int length)
        {
            var header = Program.ToStruct<zap_phys_t>(ptr, 0, length);
            if (header.zap_block_type != ZapBlockType.HEADER)
                throw new Exception();
            if (header.zap_magic != ZAP_MAGIC)
                throw new Exception();
            if (header.zap_ptrtbl.zt_numblks != 0)
                throw new NotImplementedException();

            long startIndx = (1 << header.EmbeddedPtrtblShift);
            byte* end = ptr + length;
            for (long i = 0; i < (1L << (int)header.zap_ptrtbl.zt_shift); i++)
            {
                ulong* blkIdP = (ulong*)ptr + startIndx + i;
                if (blkIdP >= end)
                    throw new Exception();
                var blkId = *blkIdP;
                if (blkId != 0 )
                    Console.WriteLine();
            }

            var leaf = Program.ToStruct<zap_leaf_header>(ptr, (dn.DataBlkSizeSec * 512), length);

            throw new NotImplementedException();
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

        const int ZAP_LEAF_CHUNKSIZE = 24;
        const int ZAP_LEAF_ARRAY_BYTES = (ZAP_LEAF_CHUNKSIZE - 3);

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
                    //fixed (mzap_ent_phys_t* pppp = &this)
                    //{
                    //}
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
        unsafe struct zap_phys_t
        {
            public ZapBlockType zap_block_type;	/* ZBT_HEADER */
            public ulong zap_magic;		/* ZAP_MAGIC */

            public zap_table_phys zap_ptrtbl;
            public ulong zap_freeblk;		/* the next free block */
            public ulong zap_num_leafs;		/* number of leafs */
            public long zap_num_entries;	/* number of entries */
            public ulong zap_salt;		/* salt to stir into hash function */
            public ulong zap_normflags;		/* flags for u8_textprep_str() */
            public ulong zap_flags;		/* zap_flags_t */
            /*
             * This structure is followed by padding, and then the embedded
             * pointer table.  The embedded pointer table takes up second
             * half of the block.  It is accessed using the
             * ZAP_EMBEDDED_PTRTBL_ENT() macro.
             */

            public int BlockShift
            {
                get { return (int)zap_ptrtbl.zt_shift; }
            }

            public int EmbeddedPtrtblShift
            {
                get { return BlockShift - 3 - 1; }
            }

        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct zap_table_phys
        {
            public ulong zt_blk;	/* starting block number */
            public ulong zt_numblks;	/* number of blocks */
            public long zt_shift;	/* bits to index it */
            public ulong zt_nextblk;	/* next (larger) copy start block */
            public ulong zt_blks_copied; /* number source blocks copied */
        }
        enum zap_chunk_type_t : byte
        {
            ZAP_CHUNK_FREE = 253,
            ZAP_CHUNK_ENTRY = 252,
            ZAP_CHUNK_ARRAY = 251,
            ZAP_CHUNK_TYPE_MAX = 250
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct zap_leaf_phys_t
        {
            public zap_leaf_header l_hdr;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct zap_leaf_header
        {
            public ZapBlockType lh_block_type;		/* zbt_leaf */
            ulong lh_pad1;
            ulong lh_prefix;		/* hash prefix of this leaf */
            uint lh_magic;		/* zap_leaf_magic */
            ushort lh_nfree;		/* number free chunks */
            ushort lh_nentries;		/* number of entries */
            ushort lh_prefix_len;		/* num bits used to id this */

            /* above is accessable to zap, below is zap_leaf private */

            ushort lh_freelist;		/* chunk head of free list */
            byte lh_flags;		/* zlf_* flags */
            fixed byte lh_pad2[11];
        }  /* 2 24-byte chunks */

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        struct zap_leaf_chunk_t
        {
            [FieldOffset(0)]
            public zap_chunk_type_t Type;

            [FieldOffset(0)]
            public zap_leaf_entry l_entry;
            [FieldOffset(0)]
            public zap_leaf_array l_array;
            [FieldOffset(0)]
            public zap_leaf_free l_free;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct zap_leaf_entry
        {
            zap_chunk_type_t le_type; 		/* always ZAP_CHUNK_ENTRY */
            byte le_value_intlen;	/* size of value's ints */
            ushort le_next;		/* next entry in hash chain */
            ushort le_name_chunk;		/* first chunk of the name */
            ushort le_name_numints;	/* ints in name (incl null) */
            ushort le_value_chunk;	/* first chunk of the value */
            ushort le_value_numints;	/* value length in ints */
            uint le_cd;			/* collision differentiator */
            ulong le_hash;		/* hash value of the name */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct zap_leaf_array
        {
            zap_chunk_type_t la_type;		/* always ZAP_CHUNK_ARRAY */
            fixed byte la_array[ZAP_LEAF_ARRAY_BYTES];
            ushort la_next;		/* next blk or CHAIN_END */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct zap_leaf_free
        {
            zap_chunk_type_t lf_type;		/* always ZAP_CHUNK_FREE */
            fixed byte lf_pad[ZAP_LEAF_ARRAY_BYTES];
            ushort lf_next;	/* next in free list, or CHAIN_END */
        }
    }
}
