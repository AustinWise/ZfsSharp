using System;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    public enum ZfsItemType
    {
        None = 0,
        /// <summary>
        /// Fifo
        /// </summary>
        S_IFIFO = 0x1,
        /// <summary>
        /// Character Special Device
        /// </summary>
        S_IFCHR = 0x2,
        /// <summary>
        /// Directory
        /// </summary>
        S_IFDIR = 0x4,
        /// <summary>
        /// Block special device
        /// </summary>
        S_IFBLK = 0x6,
        /// <summary>
        /// Regular file
        /// </summary>
        S_IFREG = 0x8,
        /// <summary>
        /// Symbolic Link
        /// </summary>
        S_IFLNK = 0xA,
        /// <summary>
        /// Socket
        /// </summary>
        S_IFSOCK = 0xC,
        /// <summary>
        /// Door
        /// </summary>
        S_IFDOOR = 0xD,
        /// <summary>
        /// Event Port
        /// </summary>
        S_IFPORT = 0xE,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct sa_hdr_phys_t
    {
        const uint SA_MAGIC = 0x2F505A;

        uint sa_magic;
        ushort sa_layout_info;  /* Encoded with hdrsize and layout number */
        public fixed ushort sa_lengths[1];	/* optional sizes for variable length attrs */
        /* ... Data follows the lengths.  */

        public int hdrsz
        {
            get { return (sa_layout_info >> 10) * 8; }
        }
        public int layout
        {
            get { return sa_layout_info & 0x3FF; }
        }

        public void VerifyMagic()
        {
            if (sa_magic != SA_MAGIC)
                throw new Exception();
        }
    }
    enum zpl_attr_t : short
    {
        ZPL_ATIME = 0,
        ZPL_MTIME,
        ZPL_CTIME,
        ZPL_CRTIME,
        ZPL_GEN,
        ZPL_MODE,
        ZPL_SIZE,
        ZPL_PARENT,
        ZPL_LINKS,
        ZPL_XATTR,
        ZPL_RDEV,
        ZPL_FLAGS,
        ZPL_UID,
        ZPL_GID,
        ZPL_PAD,
        ZPL_ZNODE_ACL,
        ZPL_DACL_COUNT,
        ZPL_SYMLINK,
        ZPL_SCANSTAMP,
        ZPL_DACL_ACES,
        //ZPL_END
    }
}
