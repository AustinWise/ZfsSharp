using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp
{
    class Zap
    {
        readonly Dmu mDmu;

        public Zap(Dmu dmu)
        {
            mDmu = dmu;
        }

        unsafe public Dictionary<string, object> Parse(dnode_phys_t dn)
        {
            var zapBytes = mDmu.Read(dn);
            fixed (byte* ptr = zapBytes)
            {
                mzap_phys_t zapHeader = (mzap_phys_t)Marshal.PtrToStructure(new IntPtr(ptr), typeof(mzap_phys_t));

                if (zapHeader.BlockType == ZapBlockType.MICRO)
                    return ParseMicro(ptr, zapBytes.Length).ToDictionary(d => d.Key, d => (object)d.Value);
                else if (zapHeader.BlockType == ZapBlockType.HEADER)
                    return ParseFat(dn, ptr, zapBytes.Length);
                else
                    throw new NotSupportedException();
            }
        }


        public Dictionary<string, long> GetDirectoryEntries(objset_phys_t objectSet, long objectId)
        {
            return GetDirectoryEntries(mDmu.ReadFromObjectSet(objectSet, objectId));
        }

        unsafe public Dictionary<string, long> GetDirectoryEntries(dnode_phys_t dn, bool skipUnexpectedValues = false)
        {
            var zapBytes = mDmu.Read(dn);
            fixed (byte* ptr = zapBytes)
            {
                mzap_phys_t zapHeader = (mzap_phys_t)Marshal.PtrToStructure(new IntPtr(ptr), typeof(mzap_phys_t));

                if (zapHeader.BlockType == ZapBlockType.MICRO)
                    return ParseMicro(ptr, zapBytes.Length);
                else if (zapHeader.BlockType == ZapBlockType.HEADER)
                {
                    var fat = ParseFat(dn, ptr, zapBytes.Length);
                    var ret = new Dictionary<string, long>();
                    foreach (var kvp in fat)
                    {
                        var data = (long[])kvp.Value;
                        if (data.Length != 1)
                        {
                            if (skipUnexpectedValues)
                                continue;
                            throw new Exception("Directory entry '" + kvp.Key + "' points to more than one object id!");
                        }
                        ret.Add(kvp.Key, data[0]);
                    }
                    return ret;
                }
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
                    continue;
                if (entry.CD != 0)
                    throw new NotImplementedException();
                ret.Add(entry.Name, entry.Value);
            }
            return ret;
        }

        unsafe Dictionary<string, object> ParseFat(dnode_phys_t dn, byte* ptr, int length)
        {
            var ret = new Dictionary<string, object>();
            var header = Program.ToStruct<zap_phys_t>(ptr, 0, length);
            var bs = highBit(dn.DataBlkSizeSec * 512);

            if (header.zap_block_type != ZapBlockType.HEADER)
                throw new Exception();
            if (header.zap_magic != ZAP_MAGIC)
                throw new Exception();
            if (header.zap_ptrtbl.zt_numblks != 0)
                throw new NotImplementedException("Only embedded pointer tables currently supported.");

            //read the pointer table
            long startIndx = (1 << header.EmbeddedPtrtblShift);
            byte* end = ptr + length;
            var blkIds = new Dictionary<long, bool>();
            for (long i = 0; i < (1L << (int)header.zap_ptrtbl.zt_shift); i++)
            {
                long* blkIdP = (long*)ptr + startIndx + i;
                if (blkIdP >= end)
                    throw new Exception();
                var blkId = *blkIdP;
                if (blkId != 0)
                    blkIds[blkId] = true;
            }

            //read the leaves
            foreach (var blkid in blkIds.Keys)
            {
                var offset = blkid << bs;
                var leaf = Program.ToStruct<zap_leaf_header>(ptr, offset, length);
                if (leaf.lh_magic != ZAP_LEAF_MAGIC)
                    throw new Exception();
                int numHashEntries = 1 << (bs - 5);
                int numChunks = ((1 << bs) - 2 * numHashEntries) / ZAP_LEAF_CHUNKSIZE - 2;

                var hashEntries = new Dictionary<ushort, bool>();

                offset += Marshal.SizeOf(typeof(zap_leaf_header));
                for (int i = 0; i < numHashEntries; i++)
                {
                    ushort* hashPtr = (ushort*)(ptr + offset);
                    if (hashPtr > end)
                        throw new Exception();
                    var loc = *hashPtr;
                    if (loc != 0xffff)
                        hashEntries[loc] = true;
                    offset += 2;
                }

                foreach (var hashLoc in hashEntries.Keys)
                {
                    var chunk = Program.ToStruct<zap_leaf_chunk_t>(ptr, offset + sizeof(zap_leaf_chunk_t) * hashLoc, length);
                    switch (chunk.Type)
                    {
                        case zap_chunk_type_t.ZAP_CHUNK_ENTRY:
                            var entry = chunk.l_entry;
                            var nameBytes = GetArray<byte>(ptr, length, offset, entry.le_name_chunk, entry.le_name_numints);
                            var nameLength = nameBytes.Length;
                            if (nameBytes[nameLength - 1] == 0)
                                nameLength--;
                            var nameStr = Encoding.ASCII.GetString(nameBytes, 0, nameLength);
                            var valueArray = GetValueArray(ptr, length, offset, entry);
                            ret.Add(nameStr, valueArray);
                            break;
                        case zap_chunk_type_t.ZAP_CHUNK_FREE:
                        case zap_chunk_type_t.ZAP_CHUNK_ARRAY:
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            return ret;
        }

        unsafe static object GetValueArray(byte* ptr, long ptrLength, long chunkTableOffset, zap_leaf_entry entry)
        {
            switch (entry.le_value_intlen)
            {
                case 1:
                    return GetArray<byte>(ptr, ptrLength, chunkTableOffset, entry.le_value_chunk, entry.le_value_numints);
                case 2:
                    return GetArray<short>(ptr, ptrLength, chunkTableOffset, entry.le_value_chunk, entry.le_value_numints);
                case 4:
                    return GetArray<int>(ptr, ptrLength, chunkTableOffset, entry.le_value_chunk, entry.le_value_numints);
                case 8:
                    return GetArray<long>(ptr, ptrLength, chunkTableOffset, entry.le_value_chunk, entry.le_value_numints);
                default:
                    throw new NotSupportedException();
            }
        }

        /// <typeparam name="T">Must be an interget</typeparam>
        unsafe static T[] GetArray<T>(byte* ptr, long ptrLength, long chunkTableOffset, int chunkEntryNdx, int totalEntries) where T : struct
        {
            var byteList = new List<byte>();
            int itemSize = Marshal.SizeOf(typeof(T));
            GetArray(ptr, ptrLength, chunkTableOffset, chunkEntryNdx, totalEntries * itemSize, byteList);
            var byteArray = byteList.ToArray();

            if (typeof(T) == typeof(byte))
                return (T[])(object)byteArray;
            if (typeof(T) != typeof(int) && typeof(T) != typeof(short) && typeof(T) != typeof(long))
                throw new NotSupportedException();

            for (int itemNdx = 0; itemNdx < totalEntries; itemNdx++)
            {
                int itemOffset = itemNdx * itemSize;
                //do byteswap, as these are always big endian numbers
                Program.ByteSwap(typeof(T), byteArray, itemOffset);
            }

            var ret = new List<T>();
            fixed (byte* bytes = byteArray)
            {
                for (int itemNdx = 0; itemNdx < totalEntries; itemNdx++)
                {
                    ret.Add(Program.ToStruct<T>(bytes, itemNdx * itemSize, byteArray.Length));
                }
            }
            return ret.ToArray();
        }

        unsafe static void GetArray(byte* ptr, long ptrLength, long chunkTableOffset, int chunkEntryNdx, int totalEntries, List<byte> list)
        {
            if (totalEntries <= 0)
                throw new ArgumentOutOfRangeException();
            if (chunkEntryNdx == CHAIN_END)
                throw new ArgumentOutOfRangeException();

            var chunk = Program.ToStruct<zap_leaf_chunk_t>(ptr, chunkTableOffset + sizeof(zap_leaf_chunk_t) * chunkEntryNdx, ptrLength);
            if (chunk.Type != zap_chunk_type_t.ZAP_CHUNK_ARRAY)
                throw new Exception();
            var array = chunk.l_array;

            int entriesToRead = ZAP_LEAF_ARRAY_BYTES;
            if (entriesToRead > totalEntries)
                entriesToRead = totalEntries;
            for (int i = 0; i < entriesToRead; i++)
            {
                var item = *(array.la_array + i);
                list.Add(item);
            }
            totalEntries -= entriesToRead;
            if (totalEntries != 0)
            {
                GetArray(ptr, ptrLength, chunkTableOffset, array.la_next, totalEntries, list);
            }
        }

        static int highBit(long some)
        {
            for (int i = 63; i >= 0; i--)
            {
                if (((1L << i) & some) != 0)
                    return i;
            }
            throw new Exception();
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
        const uint ZAP_LEAF_MAGIC = 0x2AB1EAF;

        const long ZAP_NEED_CD = (-1U);

        const int ZAP_LEAF_CHUNKSIZE = 24;
        const int ZAP_LEAF_ARRAY_BYTES = (ZAP_LEAF_CHUNKSIZE - 3);

        const int CHAIN_END = 0xffff;

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
                        if (*ptr == 0)
                            return null;
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

        [Flags]
        enum zap_flags_t : ulong
        {
            None = 0,
            /* Use 64-bit hash value (serialized cursors will always use 64-bits) */
            ZAP_FLAG_HASH64 = 1 << 0,
            /* Key is binary, not string (zap_add_uint64() can be used) */
            ZAP_FLAG_UINT64_KEY = 1 << 1,
            /*
             * First word of key (which must be an array of uint64) is
             * already randomly distributed.
             */
            ZAP_FLAG_PRE_HASHED_KEY = 1 << 2,
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
            public zap_flags_t zap_flags;		/* zap_flags_t */
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
            public uint lh_magic;		/* zap_leaf_magic */
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
            public zap_chunk_type_t le_type; 		/* always ZAP_CHUNK_ENTRY */
            public byte le_value_intlen;	/* size of value's ints */
            public ushort le_next;		/* next entry in hash chain */
            public ushort le_name_chunk;		/* first chunk of the name */
            public ushort le_name_numints;	/* ints in name (incl null) */
            public ushort le_value_chunk;	/* first chunk of the value */
            public ushort le_value_numints;	/* value length in ints */
            public uint le_cd;			/* collision differentiator */
            public ulong le_hash;		/* hash value of the name */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct zap_leaf_array
        {
            public zap_chunk_type_t la_type;		/* always ZAP_CHUNK_ARRAY */
            public fixed byte la_array[ZAP_LEAF_ARRAY_BYTES];
            public ushort la_next;		/* next blk or CHAIN_END */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe struct zap_leaf_free
        {
            public zap_chunk_type_t lf_type;		/* always ZAP_CHUNK_FREE */
            public fixed byte lf_pad[ZAP_LEAF_ARRAY_BYTES];
            public ushort lf_next;	/* next in free list, or CHAIN_END */
        }
    }
}
