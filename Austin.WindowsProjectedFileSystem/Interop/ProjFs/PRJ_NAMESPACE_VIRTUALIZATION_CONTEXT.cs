using Microsoft.Win32.SafeHandles;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            public class PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT : SafeHandleZeroOrMinusOneIsInvalid
            {
                public PRJ_NAMESPACE_VIRTUALIZATION_CONTEXT()
                    : base(true)
                {
                }

                protected override bool ReleaseHandle()
                {
                    PrjStopVirtualizing(this.handle);
                    return true;
                }
            }
        }
    }
}
