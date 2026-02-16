#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharpLib.VirtualDevices
{
    abstract class Vdev
    {
        private MetaSlabs? mMetaSlabs = null;
        protected readonly Vdev[] mChildren;

        protected Vdev(NvList config, IEnumerable<Vdev> children)
        {
            this.Guid = config.Get<ulong>("guid");
            this.ID = config.Get<ulong>("id");
            this.mChildren = children.ToArray();

            MetaSlabArray = config.GetOptional<ulong>("metaslab_array");
            MetaSlabShift = config.GetOptional<ulong>("metaslab_shift");
            AShift = config.GetOptional<ulong>("ashift");
            ASize = config.GetOptional<ulong>("asize");
        }

        //a bit of a layering violation
        public void InitMetaSlabs(ObjectSet mos)
        {
            if (MetaSlabArray == null || MetaSlabShift == null || AShift == null)
            {
                throw new Exception("Missing metaslab information.");
            }
            mMetaSlabs = new MetaSlabs(mos, (long)MetaSlabArray.Value, (int)MetaSlabShift.Value, (int)AShift.Value);
        }

        public void ReadBytes(Span<byte> dest, long offset)
        {
            if (mMetaSlabs != null && !mMetaSlabs.ContainsRange(offset, dest.Length))
            {
                throw new Exception("Reading unallocated data.");
            }
            ReadBytesCore(dest, offset);
        }

        protected abstract void ReadBytesCore(Span<byte> dest, long offset);

        public ulong Guid
        {
            get;
            private set;
        }
        public ulong GuidSum
        {
            get
            {
                ulong ret = this.Guid;
                foreach (var child in mChildren)
                {
                    ret += child.GuidSum;
                }
                return ret;
            }
        }
        public ulong ID { get; private set; }

        //These only populated on top-level vdevs.
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

        public static Vdev[] CreateTreeFromLabels(List<LeafVdevInfo> hdds, uberblock_t uberblock)
        {
            if (hdds.Count == 0)
                throw new ArgumentOutOfRangeException("No hard drives provided.");

            //ensure all HDDs are part of the same pool
            var poolGuid = hdds.Select(h => h.Config.Get<ulong>("pool_guid")).Distinct().ToArray();
            if (poolGuid.Length != 1)
                throw new Exception("Hard drives are part of different pools: " + string.Join(", ", poolGuid.Select(p => p.ToString())));

            var hddMap = new Dictionary<ulong, LeafVdevInfo>();
            var innerVdevConfigs = new Dictionary<ulong, NvList>();
            ulong numberOfChildren = hdds.OrderByDescending(l => l.ConfigTxg).First().Config.Get<ulong>("vdev_children");
            if (numberOfChildren == 0)
            {
                throw new Exception("Top level config contains zero children?!");
            }

            foreach (var hdd in hdds)
            {
                ulong guid = hdd.Config.Get<ulong>("guid");
                hddMap.Add(guid, hdd);
                var vdevTree = hdd.Config.Get<NvList>("vdev_tree");
                // According to
                // https://github.com/openzfs/zfs/blob/zfs-2.4.0/module/zfs/vdev_label.c#L93-L97
                // not every config in the pool is updated when a adding devices.
                // TODO: load the highest available config for each top level vdev.
                innerVdevConfigs[vdevTree.Get<ulong>("guid")] = vdevTree;
            }

            if (innerVdevConfigs.Count != (int)numberOfChildren)
            {
                throw new Exception($"We found {innerVdevConfigs.Count} but expected {numberOfChildren}.");
            }

            var innerVdevs = new List<Vdev>((int)numberOfChildren);
            ulong guidSum = poolGuid[0];
            foreach (var kvp in innerVdevConfigs)
            {
                Vdev vdev = Create(kvp.Value, hddMap);
                guidSum += vdev.GuidSum;
                innerVdevs.Add(vdev);
            }

            if (guidSum != uberblock.GuidSum)
            {
                throw new Exception($"The sum of vdev guids {guidSum} does not match the guid sum in the uberblock {uberblock.GuidSum}.");
            }

            var ret = innerVdevs.OrderBy(v => v.ID).ToArray();
            for (uint i = 0; i < ret.Length; i++)
            {
                if (ret[i].ID != i)
                    throw new Exception("Missing vdev.");
            }
            return ret;
        }

        public static Vdev[] CreateFromMosConfig(List<LeafVdevInfo> hdds, uberblock_t ub, NvList mosConfig)
        {
            var hddMap = new Dictionary<ulong, LeafVdevInfo>();
            foreach (var hdd in hdds)
            {
                ulong guid = hdd.Config.Get<ulong>("guid");
                hddMap.Add(guid, hdd);
            }

            var numberOfChildren = mosConfig.Get<ulong>("vdev_children");
            ulong poolGuid = mosConfig.Get<ulong>("pool_guid");

            NvList rootVdev = mosConfig.Get<NvList>("vdev_tree");
            NvList[] topLevelVdevConfigs = rootVdev.Get<NvList[]>("children");
            ulong rootGuid = rootVdev.Get<ulong>("guid");
            if (rootVdev.Get<string>("type") != "root")
            {
                throw new Exception("Top level vdev is not of type root.");
            }
            if (rootGuid != poolGuid)
            {
                throw new Exception($"Root vdev guid {rootGuid} does not match pool guid {poolGuid}.");
            }

            var ret = new Vdev[numberOfChildren];
            ulong guidSum = rootGuid;
            for (uint i = 0; i < numberOfChildren; i++)
            {
                NvList config = topLevelVdevConfigs[i];
                ulong id = config.Get<ulong>("id");
                if (id != i)
                {
                    throw new Exception($"Expected vdev with id {i} but got {id}.");
                }
                Vdev vdev = Create(config, hddMap);
                guidSum += vdev.GuidSum;
                ret[i] = vdev;
            }

            // TODO: validate when there are multiple top level vdevs with multiple child vdevs.
            if (guidSum != ub.GuidSum)
            {
                throw new Exception($"The sum of all vdev guids {guidSum} does not match the guid sum in the uberblock {ub.GuidSum}.");
            }

            return ret;
        }
    }
}
