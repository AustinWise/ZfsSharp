using System;

namespace ZfsSharp
{
    class MetaSlabs
    {
        //TODO: store each meta slab's range map separately
        RangeMap mRangeMap = new RangeMap();
        ObjectSet mMos;
        Dmu mDmu;

        public MetaSlabs(ObjectSet mos, Dmu dmu, long metaSlabArray, int metaSlabShift, int aShift)
        {
            mMos = mos;
            mDmu = dmu;

            var someBytes = mos.ReadContent(metaSlabArray);

            long[] ids = new long[someBytes.Length / 8];
            Buffer.BlockCopy(someBytes, 0, ids, 0, someBytes.Length);

            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id == 0)
                    continue;

                LoadEntrysForMetaSlab(id, (ulong)i << metaSlabShift, 1UL << metaSlabShift, aShift);
            }
        }

        public bool ContainsRange(long offset, long range)
        {
            return mRangeMap.ContainsRange((ulong)offset, (ulong)range);
        }

        void LoadEntrysForMetaSlab(long dnEntry, ulong start, ulong size, int sm_shift)
        {
            dnode_phys_t dn = mMos.ReadEntry(dnEntry);
            if (dn.Type != dmu_object_type_t.SPACE_MAP || dn.BonusType != dmu_object_type_t.SPACE_MAP_HEADER)
                throw new Exception("Not a space map.");

            var head = mDmu.GetBonus<space_map_obj>(dn);

            if (head.smo_object != dnEntry)
                throw new Exception();

            var someBytes = mDmu.Read(dn, 0, head.smo_objsize);
            for (int i = 0; i < someBytes.Length; i += 8)
            {
                var ent = Program.ToStruct<spaceMapEntry>(someBytes, i);
                if (ent.IsDebug)
                    continue;

                ulong offset = (ent.Offset << sm_shift) + start;
                ulong range = ent.Run << sm_shift;
                //Console.WriteLine("\t    [{4,6}]    {0}  range: {1:x10}-{2:x10}  size: {3:x6}", ent.Type, offset, offset + range, range, i / 8);
                if (ent.Type == SpaceMapEntryType.A)
                {
                    mRangeMap.AddRange(offset, range);
                }
                else if (ent.Type == SpaceMapEntryType.F)
                {
                    mRangeMap.RemoveRange(offset, range);
                }
            }
        }
    }
}
