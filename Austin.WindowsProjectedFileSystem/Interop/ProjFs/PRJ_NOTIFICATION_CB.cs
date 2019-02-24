using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            public delegate Int32 PRJ_NOTIFICATION_CB(
                in PRJ_CALLBACK_DATA callbackData,
                [MarshalAs(UnmanagedType.U1)] bool isDirectory,
                PRJ_NOTIFICATION notification,
                [MarshalAs(UnmanagedType.LPWStr)] string destinationFileName,
                ref PRJ_NOTIFICATION_PARAMETERS operationParameters);
        }
    }
}