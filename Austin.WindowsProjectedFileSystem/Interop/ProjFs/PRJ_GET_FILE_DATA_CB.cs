using System;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            public delegate Int32 PRJ_GET_FILE_DATA_CB(in PRJ_CALLBACK_DATA callbackData, UInt64 byteOffset, UInt32 length);
        }
    }
}