﻿using System;
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
        FileAttributes Attributes { get; set; }
    }
}
