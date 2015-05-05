using System;

namespace ZfsSharp
{
    class MetaSlabs
    {
        RangeMap[] mRangeMap;
        ObjectSet mMos;
        Dmu mDmu;
        long mSlabSize;

        public MetaSlabs(ObjectSet mos, Dmu dmu, long metaSlabArray, int metaSlabShift, int aShift)
        {
            mMos = mos;
            mDmu = dmu;
            mSlabSize = 1L << metaSlabShift;

            var someBytes = mos.ReadContent(metaSlabArray);
            int numberOfSlabs = someBytes.Length / 8;
            mRangeMap = new RangeMap[numberOfSlabs];

            long[] ids = new long[numberOfSlabs];
            Buffer.BlockCopy(someBytes, 0, ids, 0, someBytes.Length);

            for (int i = 0; i < numberOfSlabs; i++)
            {
                var id = ids[i];
                RangeMap map;
                if (id == 0)
                {
                    map = new RangeMap();
                }
                else
                {
                    map = LoadEntrysForMetaSlab(id, aShift);
                }
                mRangeMap[i] = map;
            }
        }

        public bool ContainsRange(long offset, long range)
        {
            long slabNdx = offset / mSlabSize;
            offset = offset % mSlabSize;
            return mRangeMap[slabNdx].ContainsRange((ulong)offset, (ulong)range);
        }

        RangeMap LoadEntrysForMetaSlab(long dnEntry, int sm_shift)
        {
            RangeMap ret = new RangeMap();

            dnode_phys_t dn = mMos.ReadEntry(dnEntry);
            if (dn.Type != dmu_object_type_t.SPACE_MAP || dn.BonusType != dmu_object_type_t.SPACE_MAP_HEADER)
                throw new Exception("Not a space map.");

            var head = mDmu.GetBonus<space_map_obj>(dn);

            if (head.smo_object != dnEntry)
                throw new Exception();

            if (head.smo_objsize > int.MaxValue)
                throw new Exception("Holy cow, this space map is greater than 2GB, what is wrong with your VDev!?!?");

            var someBytes = mDmu.Read(dn, 0, (int)head.smo_objsize);
            for (int i = 0; i < someBytes.Length; i += 8)
            {
                var ent = Program.ToStruct<spaceMapEntry>(someBytes, i);
                if (ent.IsDebug)
                    continue;

                ulong offset = (ent.Offset << sm_shift);
                ulong range = ent.Run << sm_shift;
                //Console.WriteLine("\t    [{4,6}]    {0}  range: {1:x10}-{2:x10}  size: {3:x6}", ent.Type, offset, offset + range, range, i / 8);
                if (ent.Type == SpaceMapEntryType.A)
                {
                    ret.AddRange(offset, range);
                }
                else if (ent.Type == SpaceMapEntryType.F)
                {
                    ret.RemoveRange(offset, range);
                }
            }

            return ret;
        }
    }
}
