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
        }

        public abstract IEnumerable<byte[]> ReadBytes(long offset, long count);
        public ulong Guid
        {
            get;
            private set;
        }
        public ulong ID { get; private set; }

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
                default:
                    throw new NotSupportedException("Unknown type: " + type);
            }
            throw new NotImplementedException();
        }
    }
}
