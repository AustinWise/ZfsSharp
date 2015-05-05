using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    abstract class Vdev
    {
        private MetaSlabs mMetaSlabs = null;

        protected Vdev(NvList config)
        {
            this.Guid = config.Get<ulong>("guid");
            this.ID = config.Get<ulong>("id");

            MetaSlabArray = config.GetOptional<ulong>("metaslab_array");
            MetaSlabShift = config.GetOptional<ulong>("metaslab_shift");
            AShift = config.GetOptional<ulong>("ashift");
            ASize = config.GetOptional<ulong>("asize");
        }

        //a bit of a layering violation
        public void InitMetaSlabs(ObjectSet mos, Dmu dmu)
        {
            mMetaSlabs = new MetaSlabs(mos, dmu, (long)MetaSlabArray.Value, (int)MetaSlabShift.Value, (int)AShift.Value);
        }

        public IEnumerable<byte[]> ReadBytes(long offset, int count)
        {
            if (mMetaSlabs != null && !mMetaSlabs.ContainsRange(offset, count))
            {
                throw new Exception("Reading unallocated data.");
            }
            return ReadBytesCore(offset, count);
        }

        protected abstract IEnumerable<byte[]> ReadBytesCore(long offset, int count);

        public ulong Guid
        {
            get;
            private set;
        }
        public ulong ID { get; private set; }

        public ulong? MetaSlabArray { get; private set; }
        public ulong? MetaSlabShift { get; private set; }
        public ulong? AShift { get; private set; }
        public ulong? ASize { get; private set; }

        public static Vdev Create(NvList config, Dictionary<ulong, LeafVdevInfo> leafs)
        {
            var type = config.Get<string>("type");
            switch (type)
            {
                case "mirror":
                    return new MirrorVdev(config, leafs);
                case "disk":
                case "file":
                    return new HddVdev(config, leafs[config.Get<ulong>("guid")]);
                case "raidz":
                    return new RaidzVdev(config, leafs);
                default:
                    throw new NotSupportedException("Unknown type: " + type);
            }
            throw new NotImplementedException();
        }

        public static Vdev[] CreateVdevTree(List<LeafVdevInfo> hdds)
        {
            var hddMap = new Dictionary<ulong, LeafVdevInfo>();
            var innerVdevConfigs = new Dictionary<ulong, NvList>();
            foreach (var hdd in hdds)
            {
                hddMap.Add(hdd.Config.Get<ulong>("guid"), hdd);
                var vdevTree = hdd.Config.Get<NvList>("vdev_tree");
                innerVdevConfigs[vdevTree.Get<ulong>("guid")] = vdevTree;
            }

            var innerVdevs = new List<Vdev>();
            foreach (var kvp in innerVdevConfigs)
            {
                innerVdevs.Add(Vdev.Create(kvp.Value, hddMap));
            }

            ulong calculatedTopGuid = 0;
            for (int i = 0; i < innerVdevs.Count; i++)
            {
                calculatedTopGuid += innerVdevs[i].Guid;
            }

            var ret = innerVdevs.OrderBy(v => v.ID).ToArray();
            for (uint i = 0; i < ret.Length; i++)
            {
                if (ret[i].ID != i)
                    throw new Exception("Missing vdev.");
            }
            return ret;
        }
    }
}
