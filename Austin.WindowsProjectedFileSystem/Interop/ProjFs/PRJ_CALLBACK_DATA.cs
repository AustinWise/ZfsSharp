using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct PRJ_CALLBACK_DATA
            {
                public UInt32 Size;
                public PRJ_CALLBACK_DATA_FLAGS Flags;
                public IntPtr NamespaceVirtualizationContext;
                public Int32 CommandId;
                public Guid FileId;
                public Guid DataStreamId;
                public string FilePathName;
                IntPtr VersionInfo;
                public UInt32 TriggeringProcessId;
                public string TriggeringProcessImageFileName;
                IntPtr InstanceContext;
            }
        }
    }
}