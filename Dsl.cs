using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;

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
    enum dd_used_t
    {
        DD_USED_HEAD,
        DD_USED_SNAP,
        DD_USED_CHILD,
        DD_USED_CHILD_RSRV,
        DD_USED_REFRSRV,
        //DD_USED_NUM
    }
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct dsl_dir_phys_t
    {
        const int DD_USED_NUM = 0x00000005;

        ulong creation_time; /* not actually used */
        public long head_dataset_obj;
        public long parent_obj;
        public long origin_obj;
        public long child_dir_zapobj;
        /*
         * how much space our children are accounting for; for leaf
         * datasets, == physical space used by fs + snaps
         */
        public ulong used_bytes;
        public ulong compressed_bytes;
        public ulong uncompressed_bytes;
        /* Administrative quota setting */
        public ulong quota;
        /* Administrative reservation setting */
        public ulong reserved;
        public long props_zapobj;
        public ulong deleg_zapobj; /* dataset delegation permissions */
        public ulong flags;
        public fixed ulong used_breakdown[DD_USED_NUM];
        public long clones; /* dsl_dir objects */
        fixed ulong pad[13]; /* pad out to 256 bytes for good measure */
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct dsl_dataset_phys_t
    {
        public long dir_obj;		/* DMU_OT_DSL_DIR */
        public long prev_snap_obj;	/* DMU_OT_DSL_DATASET */
        public ulong prev_snap_txg;
        public long next_snap_obj;	/* DMU_OT_DSL_DATASET */
        public long snapnames_zapobj;	/* DMU_OT_DSL_SNAP_MAP 0 for snaps */
        public ulong num_children;	/* clone/snap children; ==0 for head */
        public ulong creation_time;	/* seconds since 1970 */
        public ulong creation_txg;
        public long deadlist_obj;	/* DMU_OT_DEADLIST */
        /*
         * referenced_bytes, compressed_bytes, and uncompressed_bytes
         * include all blocks referenced by this dataset, including those
         * shared with any other datasets.
         */
        public ulong referenced_bytes;
        public ulong compressed_bytes;
        public ulong uncompressed_bytes;
        public ulong unique_bytes;	/* only relevant to snapshots */
        /*
         * The fsid_guid is a 56-bit ID that can change to avoid
         * collisions.  The guid is a 64-bit ID that will never
         * change, so there is a small probability that it will collide.
         */
        public ulong fsid_guid;
        public ulong guid;
        public ulong flags;		/* FLAG_* */
        public blkptr_t bp;
        public long next_clones_obj;	/* DMU_OT_DSL_CLONES */
        public long props_obj;		/* DMU_OT_DSL_PROPS for snaps */
        public long userrefs_obj;	/* DMU_OT_USERREFS */
        fixed ulong pad[5]; /* pad out to 320 bytes for good measure */
    }
}
