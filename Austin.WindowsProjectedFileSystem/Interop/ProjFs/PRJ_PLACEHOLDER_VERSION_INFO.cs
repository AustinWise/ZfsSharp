using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        public static partial class ProjFs
        {
            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct PRJ_PLACEHOLDER_VERSION_INFO
            {
                public const int PRJ_PLACEHOLDER_ID_LENGTH = 128;
                fixed byte ProviderID[PRJ_PLACEHOLDER_ID_LENGTH];
                fixed byte ContentID[PRJ_PLACEHOLDER_ID_LENGTH];
            }
        }
    }
}