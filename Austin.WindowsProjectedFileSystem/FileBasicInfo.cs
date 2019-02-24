using System;
using System.IO;

namespace Austin.WindowsProjectedFileSystem
{
    public class FileBasicInfo
    {
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public long FileSize { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime ChangeTime { get; set; }
        public FileAttributes Attributes { get; set; }

        internal Interop.ProjFs.PRJ_FILE_BASIC_INFO GoNative()
        {
            var ret = new Interop.ProjFs.PRJ_FILE_BASIC_INFO()
            {
                FileSize = this.FileSize,
                IsDirectory = this.IsDirectory,
                FileAttributes = (uint)this.Attributes,
            };
            return ret;
        }
    }
}
