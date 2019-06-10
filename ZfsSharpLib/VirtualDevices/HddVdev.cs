using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ZfsSharpLib.HardDisks;

namespace ZfsSharpLib.VirtualDevices
{
    class HddVdev : Vdev
    {
        readonly HardDisk mHdd;
        public HddVdev(NvList config, LeafVdevInfo hdd)
            : base(config)
        {
            this.mHdd = hdd.HDD;
        }

        protected override void ReadBytesCore(Span<byte> dest, long offset)
        {
            mHdd.ReadBytes(dest, offset);
        }
    }
}
