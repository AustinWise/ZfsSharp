using System;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    [StructLayout(LayoutKind.Sequential)]
    struct ddt_key_t
    {
        public zio_cksum_t cksum;
        ulong prop;

        public zio_compress Compress
        {
            get { return (zio_compress)((prop >> 32) & 0xff); }
        }

        public ushort PSize
        {
            get { return (ushort)((prop >> 16) & 0xffff); }
        }

        public ushort LSize
        {
            get { return (ushort)(prop & 0xffff); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct ddt_phys_t
    {
        public dva_t dva1;
        public dva_t dva2;
        public dva_t dva3;
        public UInt64 refcnt;
        public UInt64 phys_birth;
    }

    enum ddt_type
    {
        DDT_TYPE_ZAP = 0,
        DDT_TYPES
    }

    enum ddt_phys_type
    {
        DDT_PHYS_DITTO = 0,
        DDT_PHYS_SINGLE = 1,
        DDT_PHYS_DOUBLE = 2,
        DDT_PHYS_TRIPLE = 3,
        DDT_PHYS_TYPES
    }

    enum ddt_class
    {
        DDT_CLASS_DITTO = 0,
        DDT_CLASS_DUPLICATE,
        DDT_CLASS_UNIQUE,
        DDT_CLASSES
    }
}
