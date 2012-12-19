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

        Stream GetStream(long offset, long size);
        Stream GetStream();

        long Length { get; }
    }
}
