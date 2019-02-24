using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            public delegate Int32 PRJ_GET_DIRECTORY_ENUMERATION_CB(in PRJ_CALLBACK_DATA callbackData, in Guid enumerationId, [MarshalAs(UnmanagedType.LPWStr)] string searchExpression, IntPtr dirEntryBufferHandle);
        }
    }
}