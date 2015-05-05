using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ZfsSharp.HardDisks;

namespace ZfsSharp.VirtualDevices
{
    class HddVdev : Vdev
    {
        readonly HardDisk mHdd;
        public HddVdev(NvList config, LeafVdevInfo hdd)
            : base(config)
        {
            this.mHdd = hdd.HDD;
        }

        protected override IEnumerable<byte[]> ReadBytesCore(long offset, int count)
        {
            yield return mHdd.ReadBytes(offset, count);
        }
    }
}
