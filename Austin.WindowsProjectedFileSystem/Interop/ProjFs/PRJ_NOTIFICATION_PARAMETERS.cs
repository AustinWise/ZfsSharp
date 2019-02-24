using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            [StructLayout(LayoutKind.Explicit)]
            public struct PRJ_NOTIFICATION_PARAMETERS
            {
                [FieldOffset(0)]
                public PRJ_NOTIFY_TYPES PostCreate;
                [FieldOffset(0)]
                public PRJ_NOTIFY_TYPES FileRenamed;
                [FieldOffset(0), MarshalAs(UnmanagedType.U1)]
                public bool FileDeletedOnHandleClose;
            }
        }
    }
}