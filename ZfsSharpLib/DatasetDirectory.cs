using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    public class DatasetDirectory
    {
        private objset_phys_t mMos;
        private Zap mZap;
        private Dmu mDmu;
        private Zio mZio;
        private dsl_dir_phys_t mDslDir;
        private Dictionary<string, long> mSnapShots = new Dictionary<string, long>();

        internal DatasetDirectory(objset_phys_t mos, long objectid, string name, Zap zap, Dmu dmu, Zio zio)
        {
            this.mMos = mos;
            this.mZap = zap;
            this.mDmu = dmu;
            this.mZio = zio;
            this.Name = name;
            this.Type = DataSetType.MetaData;

            var rootDslObj = dmu.ReadFromObjectSet(mos, objectid);
            if (rootDslObj.Type != dmu_object_type_t.DSL_DIR)
                throw new NotSupportedException("Expected DSL_DIR dnode.");
            if (rootDslObj.BonusType != dmu_object_type_t.DSL_DIR)
                throw new NotSupportedException("Expected DSL_DIR bonus.");
            mDslDir = dmu.GetBonus<dsl_dir_phys_t>(rootDslObj);
            var rootDslProps = zap.Parse(dmu.ReadFromObjectSet(mos, mDslDir.props_zapobj));

            Dictionary<string, long> clones;
            if (mDslDir.clones != 0)
            {
                clones = zap.GetDirectoryEntries(mos, mDslDir.clones);
            }

            if (mDslDir.head_dataset_obj == 0)
                return; //probably meta data, like $MOS or $FREE
            var rootDataSetObj = dmu.ReadFromObjectSet(mos, mDslDir.head_dataset_obj);
            if (!IsDataSet(rootDataSetObj))
                throw new Exception("Not a dataset!");
            if (rootDataSetObj.BonusType != dmu_object_type_t.DSL_DATASET)
                throw new Exception("Missing dataset bonus!");
            var headDs = dmu.GetBonus<dsl_dataset_phys_t>(rootDataSetObj);

            if (headDs.bp.IsHole && mDslDir.origin_obj == 0)
                return; //this is $ORIGIN

            if (headDs.snapnames_zapobj != 0)
            {
                mSnapShots = mZap.GetDirectoryEntries(mMos, headDs.snapnames_zapobj);
            }

            if (headDs.bp.Type != dmu_object_type_t.OBJSET)
                throw new Exception("Expected OBJSET.");
            var headDsObjset = zio.Get<objset_phys_t>(headDs.bp);
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
            return new Zpl(mMos, objectid, mZap, mDmu, mZio);
        }

        public IEnumerable<KeyValuePair<string, Zpl>> GetZfsSnapShots()
        {
            return mSnapShots.Select(snap => new KeyValuePair<string, Zpl>(snap.Key, GetZfs(snap.Value)));
        }

        public IEnumerable<KeyValuePair<string, long>> GetChildren()
        {
            if (mDslDir.child_dir_zapobj == 0)
                return Enumerable.Empty<KeyValuePair<string, long>>();
            return mZap.GetDirectoryEntries(mMos, mDslDir.child_dir_zapobj);
        }

        internal static bool IsDataSet(dnode_phys_t dn)
        {
            return dn.Type == dmu_object_type_t.DSL_DATASET || (dn.IsNewType && dn.NewType == dmu_object_byteswap.DMU_BSWAP_ZAP);
        }
    }
}
