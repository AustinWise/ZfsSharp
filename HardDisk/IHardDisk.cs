using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZfsSharp.HardDisk
{
    interface IHardDisk
    {
        void Get<T>(long offset, out T @struct) where T : struct;

        long Length { get; }
    }
}
