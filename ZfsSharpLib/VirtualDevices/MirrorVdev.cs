using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    class MirrorVdev : Vdev
    {
        readonly Vdev[] mVdevs;
        public MirrorVdev(NvList config, Dictionary<ulong, LeafVdevInfo> leafs)
            : base(config)
        {
            this.mVdevs = config.Get<NvList[]>("children")
                .Select(child => Vdev.Create(child, leafs))
                .ToArray();
        }

        protected override IEnumerable<byte[]> ReadBytesCore(long offset, int count)
        {
            return mVdevs.SelectMany(v => v.ReadBytes(offset, count));
        }

    }
}
