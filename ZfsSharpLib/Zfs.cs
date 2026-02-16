using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ZfsSharpLib.VirtualDevices;

namespace ZfsSharpLib
{
    public class Zfs : IDisposable
    {
        const string ROOT_DATASET = "root_dataset";
        const string CONFIG = "config";
        const string DDT_STATISTICS = "DDT-statistics";
        const ulong SUPPORTED_VERSION = 5000;

        readonly static FrozenSet<string> sSupportReadFeatures = FrozenSet.Create([
            "com.delphix:hole_birth", //as far as I can tell this just means that hole block pointers will have their birth fields filled in
            "com.delphix:extensible_dataset", //this means a DSL_DATASET DN contains ZAP entries
            "org.illumos:lz4_compress",
            "org.freebsd:zstd_compress",
            "com.delphix:embedded_data",
            "com.klarasystems:vdev_zaps_v2", // An extra ZAP for storing properties on the root VDev.
            "com.delphix:head_errlog", // We don't read the error log, so we ignore this.
            "org.open-zfs:large_blocks", // Support for blocks larger than 128KB
        ]);

        List<LeafVdevInfo> mHdds;
        Zio mZio;
        Dictionary<string, long> mObjDir;
        NvList mConfig;
        ObjectSet mMos;

        public string Name { get; set; }
        public ulong Txg { get; private set; }

        [Conditional("DEBUG")]
        static unsafe void assertStructSize<T>(int size) where T : unmanaged
        {
            int measuredSize = sizeof(T);
            Debug.Assert(measuredSize == size, $"Expected struct {typeof(T).Name} to be {size} bytes, but it is {measuredSize} bytes.");
        }

        /// <summary>
        /// Create a Zfs pool from a collection of leaf vdevs.
        /// </summary>
        /// <remarks>
        /// This constructor takes ownership of the vdevs. They will be disposed either in the constructor
        /// if it throws, or in the Dispose() method.
        /// </remarks>
        /// <param name="vdevs"></param>
        public Zfs(IEnumerable<LeafVdevInfo> vdevs)
        {
            //make sure we correctly set the size of structs
            assertStructSize<zio_gbh_phys_t>(zio_gbh_phys_t.SPA_GANGBLOCKSIZE);
            assertStructSize<dnode_phys_t>(dnode_phys_t.DNODE_SIZE);
            assertStructSize<objset_phys_t>(objset_phys_t.OBJSET_PHYS_SIZE_V3);

            mHdds = [.. vdevs];

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

            //make sure we support enough features to read the MOS
            foreach (var hdd in mHdds)
            {
                if (hdd.Uberblock.Version != SUPPORTED_VERSION)
                    throw new Exception("Unsupported version.");
                CheckVersion(hdd.Config);
            }

            // While exported pools typically have the same txg on all disk labels, spa_sync_rewrite_vdev_config
            // will write labels to only 3 vdevs at a time when there are no configuration changes.
            // So we need to find the highest txg to read pools that were not cleanly exported.
            var ub = mHdds.OrderByDescending(hdd => hdd.Txg).First().Uberblock;
            if (ub.Txg == 0)
                throw new Exception("Root block pointer's transaction group is zero!");

            LoadMosAndConfig(Vdev.CreateTreeFromLabels(mHdds, ub), ub);

            // Recreate the VDEV tree based on the config in the MOS, which has a complete picture of
            // the VDEV tree.
            LoadMosAndConfig(Vdev.CreateFromMosConfig(mHdds, ub, mConfig), ub);

            // Changes to how space maps work are treated as "features required to write" for the purposes of OpenZFS.
            // So we don't need to be able to read them to read data. But they act as as sanity check that we are reading
            // allocated data.
            // TODO: implement reading space map logs to be able to reenable this.
            // TODO: implement spacemap v2 to reenable this.
            // mZio.InitMetaSlabs(mMos);
            //the second time we will make sure that space maps contain themselves
            // mZio.InitMetaSlabs(mMos);

            CheckVersion(mConfig);
            CheckFeatures();

            this.Txg = ub.Txg;
            this.Name = mConfig.Get<string>("name");
        }

        private void LoadMosAndConfig(Vdev[] vdevs, uberblock_t ub)
        {
            mZio = new Zio(vdevs);

            mMos = new ObjectSet(mZio, ub.rootbp);
            if (mMos.Type != dmu_objset_type_t.DMU_OST_META)
                throw new Exception("Given block pointer did not point to the MOS.");

            var objectDirectory = mMos.ReadEntry(1);
            //The MOS's directory sometimes has things that don't like like directory entries.
            //For example, the "scan" entry has scrub status stuffed into as an array of longs.
            mObjDir = Zap.GetDirectoryEntries(objectDirectory, true);

            {
                var configDn = mMos.ReadEntry(mObjDir[CONFIG]);
                if (configDn.Type.LegacyType != dmu_object_type_t.PACKED_NVLIST)
                    throw new Exception("Config DN was not a packed nvlist!");
                if (configDn.BonusType != dmu_object_type_t.PACKED_NVLIST_SIZE || configDn.BonusLen != 8)
                    throw new Exception("Config DN did not have the correct bonus type!");
                int bonusLength = checked((int)configDn.GetBonus<ulong>());
                var configBytes = Program.RentBytes(bonusLength);
                configDn.Read(configBytes, 0);
                mConfig = new NvList(configBytes);
                Program.ReturnBytes(configBytes);
            }
        }

        private void CheckFeatures()
        {
            var fr = Zap.GetDirectoryEntries(mMos, mObjDir["features_for_read"]);
            var fw = Zap.GetDirectoryEntries(mMos, mObjDir["features_for_write"]);
            var ff = Zap.Parse(mMos.ReadEntry(mObjDir["feature_descriptions"])).ToDictionary(kvp => kvp.Key, kvp => Program.ReadZeroTerminatedString((byte[])kvp.Value));
            if (fw.ContainsKey("com.delphix:enabled_txg") && fw["com.delphix:enabled_txg"] > 0)
            {
                var fe = Zap.GetDirectoryEntries(mMos, mObjDir["feature_enabled_txg"]);
            }

            // make sure we support all features required to read the pool, which can include features beyond those needed for the MOS.
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
            var unsupportedFeatures = features.Where(kvp => (bool)kvp.Value && !sSupportReadFeatures.Contains(kvp.Key)).ToList();
            if (unsupportedFeatures.Count > 0)
            {
                var featureNames = string.Join(", ", unsupportedFeatures.Select(kvp => kvp.Key));
                throw new Exception("Unsupported features: " + featureNames);
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
