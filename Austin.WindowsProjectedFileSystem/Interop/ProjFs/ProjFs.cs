using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        public static partial class ProjFs
        {
            [DllImport(ProjectedFfLibDll, ExactSpelling = true, CharSet = CharSet.Unicode)]
            public static extern Int32 PrjMarkDirectoryAsPlaceholder(string rootPathName, string targetPathName, IntPtr versionInfo, in Guid virtualizationInstanceID);

            [DllImport(ProjectedFfLibDll, ExactSpelling = true, CharSet = CharSet.Unicode)]
            public static extern Int32 PrjStartVirtualizing(string virtualizationRootPath, IntPtr callbacks, IntPtr instanceContext, IntPtr options, out PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT namespaceVirtualizationContext);

            [DllImport(ProjectedFfLibDll, ExactSpelling = true, CharSet = CharSet.Unicode)]
            static extern Int32 PrjStopVirtualizing(IntPtr namespaceVirtualizationContext);

            [DllImport(ProjectedFfLibDll, ExactSpelling = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool PrjFileNameMatch(string fileNameToCheck, string pattern);

            [DllImport(ProjectedFfLibDll, ExactSpelling = true, CharSet = CharSet.Unicode)]
            public static extern Int32 PrjFillDirEntryBuffer(string fileName, in PRJ_FILE_BASIC_INFO fileBasicInfo, IntPtr dirEntryBufferHandle);

            [DllImport(ProjectedFfLibDll, ExactSpelling = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool PrjDoesNameContainWildCards(string fileName);
        }
    }
}
