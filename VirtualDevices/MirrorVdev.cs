using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    class MirrorVdev : Vdev
    {
        readonly Vdev[] mVdevs;
        public MirrorVdev(Vdev[] vdevs)
        {
            this.mVdevs = (Vdev[])vdevs.Clone();
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
