using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ZfsSharp.VirtualDevices;

namespace ZfsSharp
{
    class MetaSlabs
    {
        objset_phys_t mMos;
        Dmu mDmu;

        public MetaSlabs(objset_phys_t mos, Dmu dmu, Vdev vdev)
        {
            mMos = mos;
            mDmu = dmu;

            var dn = dmu.ReadFromObjectSet(mos, (long)vdev.MetaSlabArray.Value);
            var someBytes = dmu.Read(dn);

            for (int i = 0; i < someBytes.Length; i += sizeof(long))
            {
                var id = Program.ToStruct<long>(someBytes, i);
                if (id == 0)
                    continue;

                LoadEntrysForMetaSlab(id, (ulong)i << vdev.MetaSlabShift, 1UL << vdev.MetaSlabShift, vdev.AShift);
            }
        }

        void LoadEntrysForMetaSlab(long dnEntry, ulong start, ulong size, int sm_shift)
        {
            dnode_phys_t dn = mDmu.ReadFromObjectSet(mMos, dnEntry);
            if (dn.Type != dmu_object_type_t.SPACE_MAP || dn.BonusType != dmu_object_type_t.SPACE_MAP_HEADER)
                throw new Exception("Not a space map.");

            var head = mDmu.GetBonus<space_map_obj>(dn);

            if (head.smo_object != dnEntry)
                throw new Exception();

            var someBytes = mDmu.Read(dn, 0, head.smo_objsize);
            var entries = new List<spaceMapEntry>();
            for (int i = 0; i < someBytes.Length; i += 8)
            {
                var ent = Program.ToStruct<spaceMapEntry>(someBytes, i);
                if (ent.IsDebug)
                    continue;
                //Console.WriteLine("{0}: {1} {2}", ent.Type, (ent.Offset << sm_shift) + start, ent.Run << sm_shift);
                Console.WriteLine("{0} {1:x8} {2:x6}", ent.Type, ent.Offset << sm_shift, ent.Run << sm_shift);
                entries.Add(ent);
            }
        }

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
}
