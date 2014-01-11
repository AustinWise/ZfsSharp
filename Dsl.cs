using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class Dsl
    {
        const string ROOT_DATASET = "root_dataset";
        const string CONFIG = "config";

        objset_phys_t mMos;
        Dictionary<string, long> mObjDir;
        NvList mConfig;
        Zap mZap;
        Dmu mDmu;
        Zio mZio;

        public Dsl(blkptr_t rootbp, Zap zap, Dmu dmu, Zio zio)
        {
            mZap = zap;
            mDmu = dmu;
            mZio = zio;

            mMos = zio.Get<objset_phys_t>(rootbp);
            if (mMos.Type != dmu_objset_type_t.DMU_OST_META)
                throw new Exception("Given block pointer did not point to the MOS.");

            mZio.InitMetaSlabs(mMos, dmu);
            //the second time we will make sure that space maps contain themselves
            mZio.InitMetaSlabs(mMos, dmu);

            dnode_phys_t objectDirectory = dmu.ReadFromObjectSet(mMos, 1);
            mObjDir = zap.GetDirectoryEntries(objectDirectory);

            var configDn = dmu.ReadFromObjectSet(mMos, mObjDir[CONFIG]);
            mConfig = new NvList(new MemoryStream(dmu.Read(configDn)));

            var fr = mZap.GetDirectoryEntries(dmu.ReadFromObjectSet(mMos, mObjDir["features_for_read"]));
            var fw = mZap.GetDirectoryEntries(dmu.ReadFromObjectSet(mMos, mObjDir["features_for_write"]));
            var ff = mZap.Parse(dmu.ReadFromObjectSet(mMos, mObjDir["feature_descriptions"])).ToDictionary(kvp => kvp.Key, kvp => Encoding.ASCII.GetString((byte[])kvp.Value));
            if (fw.ContainsKey("com.delphix:enabled_txg") && fw["com.delphix:enabled_txg"] > 0)
            {
                var fe = mZap.GetDirectoryEntries(dmu.ReadFromObjectSet(mMos, mObjDir["feature_enabled_txg"]));
            }
        }

        public Zpl GetDataset(long objid)
        {
            return new Zpl(mMos, objid, mZap, mDmu, mZio);
        }

        public Zpl GetRootDataSet()
        {
            return new Zpl(mMos, mObjDir[ROOT_DATASET], mZap, mDmu, mZio);
        }

        public Dictionary<string, long> ListDataSet()
        {
            var dic = new Dictionary<string, long>();
            listDataSetName(mObjDir[ROOT_DATASET], mConfig.Get<string>("name"), dic);
            return dic;
        }

        void listDataSetName(long objectId, string nameBase, Dictionary<string, long> dic)
        {
            dic.Add(nameBase, objectId);

            var rootDslObj = mDmu.ReadFromObjectSet(mMos, objectId);
            dsl_dir_phys_t dslDir = mDmu.GetBonus<dsl_dir_phys_t>(rootDslObj);

            var childZapObjid = dslDir.child_dir_zapobj;
            if (childZapObjid == 0)
                return;

            var children = mZap.GetDirectoryEntries(mDmu.ReadFromObjectSet(mMos, childZapObjid));

            foreach (var kvp in children)
            {
                listDataSetName(kvp.Value, nameBase + "/" + kvp.Key, dic);
            }
        }
    }
}
