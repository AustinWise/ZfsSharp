using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    abstract class Vdev
    {
        public abstract IEnumerable<byte[]> ReadBytes(long offset, long count);
    }
}
