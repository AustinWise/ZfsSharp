using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct PRJ_PLACEHOLDER_INFO
            {
                public PRJ_FILE_BASIC_INFO FileBasicInfo;
                public UInt32 EaBufferSize;
                public UInt32 OffsetToFirstEa;
                public UInt32 SecurityBufferSize;
                public UInt32 OffsetToSecurityDescriptor;
                public UInt32 StreamsInfoBufferSize;
                public UInt32 OffsetToFirstStreamInfo;
                public PRJ_PLACEHOLDER_VERSION_INFO VersionInfo;
                fixed byte VariableData[1];
            }
        }
    }
}