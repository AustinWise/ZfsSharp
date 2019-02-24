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
            if (this.ChangeTime.Year >= 1601)
                ret.ChangeTime = this.ChangeTime.ToFileTimeUtc();
            if (this.CreationTime.Year >= 1601)
                ret.CreationTime = this.CreationTime.ToFileTimeUtc();
            if (this.LastAccessTime.Year >= 1601)
                ret.LastAccessTime = this.LastAccessTime.ToFileTimeUtc();
            if (this.LastWriteTime.Year >= 1601)
                ret.LastWriteTime = this.LastWriteTime.ToFileTimeUtc();
            return ret;
        }
    }
}
