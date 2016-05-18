using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ZfsSharp
{
    static class Extensions
    {
        public static ArraySegment<T> SubSegment<T>(this ArraySegment<T> parent, int offset, int count)
        {
            if (parent.Array == null)
                throw new ArgumentNullException(nameof(parent), "Parent array is null.");
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Negative offset.");
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Negative count.");

            if (offset >= parent.Count)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset is beyond the end of the parent.");
            if (offset + count > parent.Count)
                throw new ArgumentOutOfRangeException(nameof(count), "Sub segment extends beyond the end of the parent.");

            return new ArraySegment<T>(parent.Array, parent.Offset + offset, count);
        }

        public static T Get<T>(this ArraySegment<T> seg, int offset)
        {
            if (seg.Offset + offset >= seg.Count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            return seg.Array[seg.Offset + offset];
        }

        public static void Set<T>(this ArraySegment<T> seg, int offset, T value)
        {
            if (seg.Offset + offset >= seg.Count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            seg.Array[seg.Offset + offset] = value;
        }

        public unsafe static void ZeroMemory(this ArraySegment<byte> dest)
        {
            fixed (byte* pDest = dest.Array)
            {
                Unsafe.InitBlock(pDest + dest.Offset, 0, (uint)dest.Count);
            }
        }
    }
}
