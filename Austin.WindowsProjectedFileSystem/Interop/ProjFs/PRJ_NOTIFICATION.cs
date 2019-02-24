using System;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            public enum PRJ_NOTIFICATION : UInt32
            {
                FILE_OPENED = 0x00000002,
                NEW_FILE_CREATED = 0x00000004,
                FILE_OVERWRITTEN = 0x00000008,
                PRE_DELETE = 0x00000010,
                PRE_RENAME = 0x00000020,
                PRE_SET_HARDLINK = 0x00000040,
                FILE_RENAMED = 0x00000080,
                HARDLINK_CREATED = 0x00000100,
                FILE_HANDLE_CLOSED_NO_MODIFICATION = 0x00000200,
                FILE_HANDLE_CLOSED_FILE_MODIFIED = 0x00000400,
                FILE_HANDLE_CLOSED_FILE_DELETED = 0x00000800,
                FILE_PRE_CONVERT_TO_FULL = 0x00001000,
            }
        }
    }
}