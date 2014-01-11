using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class DatasetDirectory
    {
        private objset_phys_t mMos;
        private Zap mZap;
        private Dmu mDmu;
        private Zio mZio;
        private dsl_dir_phys_t mDslDir;
        private Dictionary<string, long> mSnapShots = new Dictionary<string, long>();

        public DatasetDirectory(objset_phys_t mos, long objectid, string name, Zap zap, Dmu dmu, Zio zio)
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
            mDslDir = dmu.GetBonus<dsl_dir_phys_t>(rootDslObj);
            var rootDslProps = zap.Parse(dmu.ReadFromObjectSet(mos, mDslDir.props_zapobj));

            var children = zap.Parse(mDmu.ReadFromObjectSet(mos, mDslDir.child_dir_zapobj));
            Dictionary<string, long> clones;
            if (mDslDir.clones != 0)
            {
                clones = zap.GetDirectoryEntries(mDmu.ReadFromObjectSet(mos, mDslDir.clones));
            }

            if (mDslDir.head_dataset_obj == 0)
                return; //probably meta data, like $MOS or $FREE
            var rootDataSetObj = dmu.ReadFromObjectSet(mos, mDslDir.head_dataset_obj);
            if (rootDataSetObj.Type != dmu_object_type_t.DSL_DATASET)
                throw new Exception("Not a DSL_DIR.");
            var headDs = dmu.GetBonus<dsl_dataset_phys_t>(rootDataSetObj);

            if (headDs.bp.IsHole && mDslDir.origin_obj == 0)
                return; //this is $ORIGIN

            if (headDs.snapnames_zapobj != 0)
            {
                mSnapShots = mZap.GetDirectoryEntries(mDmu.ReadFromObjectSet(mMos, headDs.snapnames_zapobj));
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
    }
}
