using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZfsSharp
{
    interface IHardDisk
    {
        void Get<T>(long offset, out T @struct) where T : struct;

        byte[] ReadBytes(long offset, long count);

        long Length { get; }
    }
}
