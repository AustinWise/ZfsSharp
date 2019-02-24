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
        }
    }
}
