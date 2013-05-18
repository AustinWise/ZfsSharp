using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    class RaidzVdev : Vdev
    {
        readonly Vdev[] mVdevs;
        public RaidzVdev(NvList config, Dictionary<ulong, LeafVdevInfo> leafs)
            : base(config)
        {
            this.mVdevs = config.Get<NvList[]>("children")
                .Select(child => Vdev.Create(child, leafs))
                .OrderBy(child => child.ID)
                .ToArray();

            if (config.Get<ulong>("nparity") != 1)
                throw new NotSupportedException("Only supporting 1 parity disk.");
        }

        public override IEnumerable<byte[]> ReadBytes(long offset, long count)
        {
            for (int i = 0; i < mVdevs.Length; i++)
            {
                foreach (var bytes in mVdevs[i].ReadBytes(offset, count))
                {
                    yield return bytes;
                }
            }
        }
    }
}
