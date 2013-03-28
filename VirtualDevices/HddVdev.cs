﻿using System;
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

        public override IEnumerable<byte[]> ReadBytes(long offset, long count)
        {
            yield return mHdd.ReadBytes(offset, count);
        }
    }
}
