using System;

namespace ZfsSharpLib
{
    class MetaSlabs
    {
        RangeMap[] mRangeMap;
        ObjectSet mMos;
        long mSlabSize;

        public MetaSlabs(ObjectSet mos, long metaSlabArray, int metaSlabShift, int aShift)
        {
            mMos = mos;
            mSlabSize = 1L << metaSlabShift;

            var slabDnode = mos.ReadEntry(metaSlabArray);
            var someBytes = Program.RentBytes(checked((int)slabDnode.AvailableDataSize));
            slabDnode.Read(someBytes, 0);

            int numberOfSlabs = someBytes.Count / 8;
            mRangeMap = new RangeMap[numberOfSlabs];
            long[] ids = new long[numberOfSlabs];
            Buffer.BlockCopy(someBytes.Array, someBytes.Offset, ids, 0, someBytes.Count);

            Program.ReturnBytes(someBytes);

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

        unsafe RangeMap LoadEntrysForMetaSlab(long dnEntry, int sm_shift)
        {
            RangeMap ret = new RangeMap();

            var dn = mMos.ReadEntry(dnEntry);
            if (dn.Type != dmu_object_type_t.SPACE_MAP || dn.BonusType != dmu_object_type_t.SPACE_MAP_HEADER)
                throw new Exception("Not a space map.");

            var head = dn.GetBonus<space_map_obj>();

            if (head.smo_object != dnEntry)
                throw new Exception();

            if (head.smo_objsize > int.MaxValue)
                throw new Exception("Holy cow, this space map is greater than 2GB, what is wrong with your VDev!?!?");

            var someBytes = Program.RentBytes((int)head.smo_objsize);
            dn.Read(someBytes, 0);
            for (int i = 0; i < someBytes.Count; i += 8)
            {
                var ent = Program.ToStruct<spaceMapEntry>(someBytes.SubSegment(i, sizeof(spaceMapEntry)));
                if (ent.IsDebug)
                    continue;

                ulong offset = ent.Offset << sm_shift;
                ulong range = ent.Run << sm_shift;
                // Console.WriteLine("\t    [{4,6}]    {0}  range: {1:x10}-{2:x10}  size: {3:x6}", ent.Type, offset, offset + range, range, i / 8);
                if (ent.Type == SpaceMapEntryType.A)
                {
                    ret.AddRange(offset, range);
                }
                else if (ent.Type == SpaceMapEntryType.F)
                {
                    ret.RemoveRange(offset, range);
                }
            }
            Program.ReturnBytes(someBytes);

            return ret;
        }
    }
}
