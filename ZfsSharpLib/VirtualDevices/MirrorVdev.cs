using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharpLib.VirtualDevices
{
    class MirrorVdev : Vdev
    {
        public MirrorVdev(NvList config, Dictionary<ulong, LeafVdevInfo> leafs)
            : base(config, config.Get<NvList[]>("children")
                .Select(child => Vdev.Create(child, leafs)))
        {
        }

        protected override void ReadBytesCore(Span<byte> dest, long offset)
        {
            //TODO: a more intelligent way of selecting which vdev to read from.
            mChildren[offset % mChildren.Length].ReadBytes(dest, offset);
        }

    }
}
