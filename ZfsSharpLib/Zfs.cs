using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using ZfsSharpLib.VirtualDevices;

namespace ZfsSharpLib
{
    public class Zfs : IDisposable
    {
        const string ROOT_DATASET = "root_dataset";
        const string CONFIG = "config";
        const string DDT_STATISTICS = "DDT-statistics";
        const ulong SUPPORTED_VERSION = 5000;

        readonly static ReadOnlyCollection<string> sSupportReadFeatures = new ReadOnlyCollection<string>(new string[]{
            "com.delphix:hole_birth", //as far as I can tell this just means that hole block pointers will have their birth fields filled in
            "com.delphix:extensible_dataset", //this means a DSL_DATASET DN contains ZAP entries
            "org.illumos:lz4_compress",
            "com.delphix:embedded_data",
        });

        List<LeafVdevInfo> mHdds;
        Zio mZio;
        Dictionary<string, long> mObjDir;
        NvList mConfig;
        ObjectSet mMos;

        static void assertStructSize<T>(int size)
        {
            int messuredSize = Program.SizeOf<T>();
            System.Diagnostics.Debug.Assert(messuredSize == size);
        }

        /// <summary></summary>
        /// <param name="directoryOrFile">Either the path to a virutal hard disk image file or a directory containing multiple images.</param>
        public Zfs(string directoryOrFile)
        {
            //make sure we correctly set the size of structs
            assertStructSize<zio_gbh_phys_t>(zio_gbh_phys_t.SPA_GANGBLOCKSIZE);

            mHdds = LeafVdevInfo.GetLeafVdevs(directoryOrFile);

            try
            {
                Load();
            }
            catch
            {
                foreach (var hdd in mHdds)
                {
                    hdd.Dispose();
                }
                throw;
            }
        }

        private void Load()
        {
            if (mHdds.Count == 0)
                throw new Exception("Did not find any hard drives.");

            //make sure we support reading the pool
            foreach (var hdd in mHdds)
            {
                CheckVersion(hdd.Config);
            }

            //ensure all HDDs are part of the same pool
            var poolGuid = mHdds.Select(h => h.Config.Get<ulong>("pool_guid")).Distinct().ToArray();
            if (poolGuid.Length != 1)
                throw new Exception("Hard drives are part of different pools: " + string.Join(", ", poolGuid.Select(p => p.ToString())));

            //ensure all HDDs agree on the transaction group id
            var txg = mHdds.Select(hdd => hdd.Uberblock.Txg).Distinct().ToArray();
            if (txg.Length != 1)
                throw new Exception("Uberblocks do not all have the same transaction group id: " + string.Join(", ", txg.Select(p => p.ToString())));

            var ub = mHdds[0].Uberblock;
            if (ub.Txg == 0)
                throw new Exception("Root block pointer's transaction group is zero!");

            var vdevs = Vdev.CreateVdevTree(mHdds);

            mZio = new Zio(vdevs);

            mMos = new ObjectSet(mZio, mZio.Get<objset_phys_t>(ub.rootbp));
            if (mMos.Type != dmu_objset_type_t.DMU_OST_META)
                throw new Exception("Given block pointer did not point to the MOS.");

            mZio.InitMetaSlabs(mMos);
            //the second time we will make sure that space maps contain themselves
            mZio.InitMetaSlabs(mMos);

            var objectDirectory = mMos.ReadEntry(1);
            //The MOS's directory sometimes has things that don't like like directory entries.
            //For example, the "scan" entry has scrub status stuffed into as an array of longs.
            mObjDir = Zap.GetDirectoryEntries(objectDirectory, true);

            if (mObjDir.ContainsKey(DDT_STATISTICS))
            {
                var ddtStats = Zap.Parse(mMos.ReadEntry(mObjDir[DDT_STATISTICS]));
                //TODO: maybe do something interesting with the stats
            }

            {
                var configDn = mMos.ReadEntry(mObjDir[CONFIG]);
                var configBytes = Program.RentBytes(checked((int)configDn.AvailableDataSize));
                configDn.Read(configBytes, 0);
                mConfig = new NvList(configBytes);
                Program.ReturnBytes(configBytes);
            }

            CheckVersion(mConfig);
            CheckFeatures();
        }

        private void CheckFeatures()
        {
            var fr = Zap.GetDirectoryEntries(mMos, mObjDir["features_for_read"]);
            var fw = Zap.GetDirectoryEntries(mMos, mObjDir["features_for_write"]);
            var ff = Zap.Parse(mMos.ReadEntry(mObjDir["feature_descriptions"])).ToDictionary(kvp => kvp.Key, kvp => Encoding.ASCII.GetString((byte[])kvp.Value));
            if (fw.ContainsKey("com.delphix:enabled_txg") && fw["com.delphix:enabled_txg"] > 0)
            {
                var fe = Zap.GetDirectoryEntries(mMos, mObjDir["feature_enabled_txg"]);
            }

            foreach (var feature in fr)
            {
                if (feature.Value != 0 && !sSupportReadFeatures.Contains(feature.Key))
                {
                    throw new Exception("Unsupported feature: " + feature.Key);
                }
            }
        }

        private static void CheckVersion(NvList cfg)
        {
            if (cfg.Get<ulong>("version") != SUPPORTED_VERSION)
                throw new Exception("Unsupported version.");

            var state = (pool_state)cfg.Get<ulong>("state");
            if (state != pool_state.ACTIVE && state != pool_state.EXPORTED)
                throw new Exception("Unknown state: " + state);

            var features = cfg.Get<NvList>("features_for_read");
            foreach (var kvp in features)
            {
                if (!sSupportReadFeatures.Contains(kvp.Key))
                    throw new Exception("Unsupported feature: " + kvp.Key);
            }
        }

        public DatasetDirectory GetRootDataset()
        {
            var dsd = new DatasetDirectory(mMos, mObjDir[ROOT_DATASET], mConfig.Get<string>("name"), mZio);
            return dsd;
        }

        public List<DatasetDirectory> GetAllDataSets()
        {
            var ret = new List<DatasetDirectory>();
            listDataSetName(mObjDir[ROOT_DATASET], mConfig.Get<string>("name"), ret);
            return ret;
        }

        void listDataSetName(long objectId, string nameBase, List<DatasetDirectory> ret)
        {
            var dsd = new DatasetDirectory(mMos, objectId, nameBase, mZio);
            ret.Add(dsd);

            foreach (var kvp in dsd.GetChildIds())
            {
                listDataSetName(kvp.Value, nameBase + "/" + kvp.Key, ret);
            }
        }

        public void Dispose()
        {
            foreach (var hdd in mHdds)
            {
                hdd.Dispose();
            }
        }
    }
}
