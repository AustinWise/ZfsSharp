using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp
{
    class Zpl
    {
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct sa_hdr_phys_t
    {
        const uint SA_MAGIC = 0x2F505A;

        uint sa_magic;
        ushort sa_layout_info;  /* Encoded with hdrsize and layout number */
        fixed ushort sa_lengths[1];	/* optional sizes for variable length attrs */
        /* ... Data follows the lengths.  */

        public int hdrsz
        {
            get { return sa_layout_info & 0x7FF; }
        }
        public int layout
        {
            get { return sa_layout_info >> 11; }
        }
    }
}
