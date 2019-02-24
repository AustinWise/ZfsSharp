using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            [StructLayout(LayoutKind.Sequential)]
            public class PRJ_CALLBACKS
            {
                public PRJ_START_DIRECTORY_ENUMERATION_CB StartDirectoryEnumerationCallback;
                public PRJ_END_DIRECTORY_ENUMERATION_CB EndDirectoryEnumerationCallback;
                public PRJ_GET_DIRECTORY_ENUMERATION_CB GetDirectoryEnumerationCallback;
                public PRJ_GET_PLACEHOLDER_INFO_CB GetPlaceholderInfoCallback;
                public PRJ_GET_FILE_DATA_CB GetFileDataCallback;

                public PRJ_QUERY_FILE_NAME_CB QueryFileNameCallback;
                public PRJ_NOTIFICATION_CB NotificationCallback;
                public PRJ_CANCEL_COMMAND_CB CancelCommandCallback;
            }
            [StructLayout(LayoutKind.Sequential)]
            public class PRJ_CALLBACKS_INTPTR
            {
                public IntPtr StartDirectoryEnumerationCallback;
                public IntPtr EndDirectoryEnumerationCallback;
                public IntPtr GetDirectoryEnumerationCallback;
                public IntPtr GetPlaceholderInfoCallback;
                public IntPtr GetFileDataCallback;

                public IntPtr QueryFileNameCallback;
                public IntPtr NotificationCallback;
                public IntPtr CancelCommandCallback;
            }
        }
    }
}
