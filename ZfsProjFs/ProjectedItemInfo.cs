using Austin.WindowsProjectedFileSystem;
using ZfsSharpLib;

namespace ZfsProjFs
{
    class ProjectedItemInfo
    {
        public string FullName;
        public FileBasicInfo ProjectedForm;
        public Zpl.ZfsItem ZfsItem;
        public ProjectedItemInfo[] Children;
    }
}
