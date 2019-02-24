using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct PRJ_FILE_BASIC_INFO
            {
                [MarshalAs(UnmanagedType.U1)]
                public bool IsDirectory;
                public Int64 FileSize;
                public Int64 CreationTime;
                public Int64 LastAccessTime;
                public Int64 LastWriteTime;
                public Int64 ChangeTime;
                public UInt32 FileAttributes;
            }
        }
    }
}