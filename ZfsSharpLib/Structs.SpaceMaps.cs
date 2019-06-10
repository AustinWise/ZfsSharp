using System.Runtime.InteropServices;

namespace ZfsSharpLib
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct space_map_obj
    {
        public long smo_object;	/* on-disk space map object */
        public long smo_objsize;	/* size of the object */
        public long smo_alloc;	/* space allocated from the map */
    }

    enum SpaceMapEntryType
    {
        A = 0,
        F = 1,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct spaceMapEntry
    {
        ulong mData;

        public spaceMapEntry(SpaceMapEntryType type, ulong offset, ulong run)
        {
            mData = 0;
        }

        public bool IsDebug
        {
            get
            {
                return (mData >> 63) == 1;
            }
        }

        // non-debug fields

        public ulong Offset
        {
            get
            {
                ulong mask = ~0UL >> 1;
                return (mData & mask) >> 16;
            }
        }

        public SpaceMapEntryType Type
        {
            get
            {
                return (SpaceMapEntryType)((mData >> 15) & 1);
            }
        }

        public ulong Run
        {
            get
            {
                return (mData & 0x7fff) + 1;
            }
        }
    }
}
