using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    abstract class Vdev
    {
        protected Vdev(NvList config)
        {
            this.Guid = config.Get<ulong>("guid");
            this.ID = config.Get<ulong>("id");

            var metaslabs = config.Where(c => c.Key == "metaslab_array").Select(c => (ulong)c.Value).ToArray();
            if (metaslabs.Length == 0)
                MetaSlabArray = null;
            else if (metaslabs.Length == 1)
                MetaSlabArray = metaslabs[0];
            else
                throw new NotSupportedException();

            MetaSlabShift = (int)config.Get<ulong>("metaslab_shift");
            AShift = (int)config.Get<ulong>("ashift");
            ASize = config.Get<ulong>("asize");
        }

        public abstract IEnumerable<byte[]> ReadBytes(long offset, long count);
        public ulong Guid
        {
            get;
            private set;
        }
        public ulong ID { get; private set; }

        public ulong? MetaSlabArray { get; private set; }
        public int MetaSlabShift { get; private set; }
        public int AShift { get; private set; }
        public ulong ASize { get; private set; }

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
    }
}
