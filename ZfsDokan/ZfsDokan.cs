using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dokan;
using System.Collections;
using System.IO;
using ZfsSharpLib;

namespace ZfsDokan
{
    class ZfsDokan : DokanOperations
    {
        private Dictionary<string, Zpl.ZfsItem> mItems = new Dictionary<string, Zpl.ZfsItem>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<Zpl.ZfsItem, string> mRevItems = new Dictionary<Zpl.ZfsItem, string>();

        public ZfsDokan(Zfs zfs)
        {
            foreach (var ds in zfs.GetAllDataSets().Where(ds => ds.Type == DataSetType.ZFS && ds.Name.Contains("var")))
            {

                loadItems(ds.GetHeadZfs().Root);

                break;
            }
        }

        void loadItems(Zpl.ZfsItem root)
        {
            var path = root.FullPath.Replace('/', '\\');
            if (mItems.ContainsKey(path))
                return;

            if (!FilterItem(root))
                return;

            mItems.Add(path, root);
            mRevItems.Add(root, path);

            if (root is Zpl.ZfsDirectory)
            {
                foreach (var f in ((Zpl.ZfsDirectory)root).GetChildren())
                {
                    loadItems(f);
                }
            }
        }

        static bool FilterItem(Zpl.ZfsItem item)
        {
            return item is Zpl.ZfsDirectory || (item is Zpl.ZfsFile && ((Zpl.ZfsFile)item).Type == ZfsItemType.S_IFREG);
        }

        public int CreateFile(string filename, FileAccess access, FileShare share, FileMode mode, FileOptions options, DokanFileInfo info)
        {
            if (!mItems.ContainsKey(filename))
                return -1;
            return 0;
        }

        public int OpenDirectory(string filename, DokanFileInfo info)
        {
            return 0;
        }

        public int CreateDirectory(string filename, DokanFileInfo info)
        {
            return -1;
        }

        public int Cleanup(string filename, DokanFileInfo info)
        {
            return 0;
        }

        public int CloseFile(string filename, DokanFileInfo info)
        {
            return 0;
        }

        public int ReadFile(string filename, byte[] buffer, ref uint readBytes, long offset, DokanFileInfo info)
        {
            //TODO
            if (!mItems.ContainsKey(filename))
                return -1;

            var item = mItems[filename] as Zpl.ZfsFile;
            if (item == null)
                return -1;

            if (offset >= item.Length || buffer.Length == 0)
                return -1;

            int bytesToRead = buffer.Length;
            if (offset + bytesToRead > item.Length)
            {
                bytesToRead = (int)(item.Length - offset);
            }

            item.GetContents(new Span<byte>(buffer, 0, bytesToRead), offset);
            readBytes = (uint)bytesToRead;
            return 0;
        }

        public int WriteFile(string filename, byte[] buffer, ref uint writtenBytes, long offset, DokanFileInfo info)
        {
            return -1;
        }

        public int FlushFileBuffers(string filename, DokanFileInfo info)
        {
            return -1;
        }

        public int GetFileInformation(string filename, FileInformation fileinfo, DokanFileInfo info)
        {
            if (!mItems.ContainsKey(filename))
                return -1;

            var item = mItems[filename];

            SetAttributes(item, fileinfo);

            return 0;
        }

        private static void SetAttributes(Zpl.ZfsItem item, FileInformation finfo)
        {
            if (item is Zpl.ZfsDirectory)
                finfo.Attributes = FileAttributes.Directory;
            else if (item is Zpl.ZfsFile)
                finfo.Attributes = FileAttributes.Normal;
            else
                throw new NotSupportedException();

            finfo.LastAccessTime = item.ATIME;
            finfo.LastWriteTime = item.MTIME;
            finfo.CreationTime = item.CTIME;
            if (item is Zpl.ZfsFile)
            {
                var zplFile = (Zpl.ZfsFile)item;
                finfo.Length = zplFile.Length;
            }
        }

        public int FindFiles(string filename, ArrayList files, DokanFileInfo info)
        {
            if (!mItems.ContainsKey(filename))
                return -1;

            var dir = mItems[filename] as Zpl.ZfsDirectory;
            if (dir == null)
                return -1;

            foreach (var item in dir.GetChildren().Where(FilterItem))
            {
                FileInformation finfo = new FileInformation();
                finfo.FileName = item.Name;
                SetAttributes(item, finfo);
                files.Add(finfo);
            }

            return 0;
        }

        public int SetFileAttributes(string filename, FileAttributes attr, DokanFileInfo info)
        {
            return -1;
        }

        public int SetFileTime(string filename, DateTime ctime, DateTime atime, DateTime mtime, DokanFileInfo info)
        {
            return -1;
        }

        public int DeleteFile(string filename, DokanFileInfo info)
        {
            return -1;
        }

        public int DeleteDirectory(string filename, DokanFileInfo info)
        {
            return -1;
        }

        public int MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            return -1;
        }

        public int SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            return -1;
        }

        public int SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            return -1;
        }

        public int LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return 0;
        }

        public int UnlockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return 0;
        }

        public int GetDiskFreeSpace(ref ulong freeBytesAvailable, ref ulong totalBytes, ref ulong totalFreeBytes, DokanFileInfo info)
        {
            freeBytesAvailable = 512 * 1024 * 1024;
            totalBytes = 1024 * 1024 * 1024;
            totalFreeBytes = 512 * 1024 * 1024;
            return 0;
        }

        public int Unmount(DokanFileInfo info)
        {
            return 0;
        }
    }
}
