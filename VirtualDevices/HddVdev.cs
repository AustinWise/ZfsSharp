using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    class HddVdev : Vdev
    {
        readonly HardDisk mHdd;
        public HddVdev(HardDisk hdd)
        {
            this.mHdd = hdd;
        }

        public override IEnumerable<byte[]> ReadBytes(long offset, long count)
        {
            yield return mHdd.ReadBytes(offset, count);
        }
    }
}
