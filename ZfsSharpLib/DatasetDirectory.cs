using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

namespace ZfsSharpLib
{
    public class DatasetDirectory
    {
        private ObjectSet mMos;
        private Zio mZio;
        private dsl_dir_phys_t mDslDir;
        private Dictionary<string, long> mSnapShots = new Dictionary<string, long>();

        internal DatasetDirectory(ObjectSet mos, long objectid, string name, Zio zio)
        {
            this.mMos = mos;
            this.mZio = zio;
            this.Name = name;
            this.Type = DataSetType.MetaData;

            var rootDslObj = mos.ReadEntry(objectid);
            if (rootDslObj.Type.LegacyType != dmu_object_type_t.DSL_DIR)
                throw new NotSupportedException("Expected DSL_DIR dnode.");
            if (rootDslObj.BonusType != dmu_object_type_t.DSL_DIR)
                throw new NotSupportedException("Expected DSL_DIR bonus.");
            mDslDir = rootDslObj.GetBonus<dsl_dir_phys_t>();
            var rootDslProps = Zap.Parse(mos, mDslDir.props_zapobj);

            Dictionary<string, long> clones;
            if (mDslDir.clones != 0)
            {
                clones = Zap.GetDirectoryEntries(mos, mDslDir.clones);
            }

            if (mDslDir.head_dataset_obj == 0)
                return; //probably meta data, like $MOS or $FREE
            var rootDataSetObj = mos.ReadEntry(mDslDir.head_dataset_obj);
            if (!IsDataSet(rootDataSetObj))
                throw new Exception("Not a dataset!");
            if (rootDataSetObj.BonusType != dmu_object_type_t.DSL_DATASET)
                throw new Exception("Missing dataset bonus!");
            var headDs = rootDataSetObj.GetBonus<dsl_dataset_phys_t>();

            if (headDs.bp.IsHole && mDslDir.origin_obj == 0)
                return; //this is $ORIGIN

            if (headDs.snapnames_zapobj != 0)
            {
                mSnapShots = Zap.GetDirectoryEntries(mMos, headDs.snapnames_zapobj);
            }

            if (headDs.bp.Type != dmu_object_type_t.OBJSET)
                throw new Exception("Expected OBJSET.");
            var headDsObjset = new ObjectSet(zio, headDs.bp);
            switch (headDsObjset.Type)
            {
                case dmu_objset_type_t.DMU_OST_ZFS:
                    this.Type = DataSetType.ZFS;
                    break;
                case dmu_objset_type_t.DMU_OST_ZVOL:
                    this.Type = DataSetType.ZVOL;
                    break;
                default:
                    throw new Exception("Unknow dataset type: " + headDsObjset.Type.ToString());
            }
        }

        public DataSetType Type { get; private set; }
        public string Name { get; private set; }

        public Zpl GetHeadZfs()
        {
            return GetZfs(mDslDir.head_dataset_obj);
        }

        private Zpl GetZfs(long objectid)
        {
            return new Zpl(mMos, objectid, mZio);
        }

        public IEnumerable<KeyValuePair<string, Zpl>> GetZfsSnapShots()
        {
            return mSnapShots.Select(snap => new KeyValuePair<string, Zpl>(snap.Key, GetZfs(snap.Value)));
        }

        internal IEnumerable<KeyValuePair<string, long>> GetChildIds()
        {
            if (mDslDir.child_dir_zapobj == 0)
                return Enumerable.Empty<KeyValuePair<string, long>>();
            return Zap.GetDirectoryEntries(mMos, mDslDir.child_dir_zapobj);
        }

        public Dictionary<string, DatasetDirectory> GetChildren()
        {
            var ret = new Dictionary<string, DatasetDirectory>();
            if (mDslDir.child_dir_zapobj != 0)
            {
                foreach (var child in Zap.GetDirectoryEntries(mMos, mDslDir.child_dir_zapobj))
                {
                    ret.Add(child.Key, new DatasetDirectory(mMos, child.Value, child.Key, mZio));
                }
            }
            return ret;
        }

        internal static bool IsDataSet(DNode dn)
        {
            var legacyType = dn.Type.LegacyType;
            if (legacyType == dmu_object_type_t.DSL_DATASET)
            {
                return true;
            }
            else if (legacyType == dmu_object_type_t.NONE)
            {
                // new type of object type
                return dn.Type.Byteswap == dmu_object_byteswap.ZAP;
            }
            else
            {
                return false;
            }
        }
    }
}
