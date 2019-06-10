using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharpLib.VirtualDevices
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

        protected override void ReadBytesCore(Span<byte> dest, long offset)
        {
            //TODO: use more than one child
            mVdevs[0].ReadBytes(dest, offset);
        }

    }
}
