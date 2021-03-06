﻿using Austin.WindowsProjectedFileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using ZfsSharpLib;

namespace ZfsProjFs
{
    class Program : IProjectedFileSystemCallbacks
    {
        Zfs mZfs;
        ProjectedFileSystem mProjFs;
        Dictionary<string, ProjectedItemInfo> mCache = new Dictionary<string, ProjectedItemInfo>();

        static void Usage()
        {
            Console.WriteLine("Usage: ZfsProjFs.exe <zfs disk image directory> <target directory>");
            Console.WriteLine("    zfs disk image directory: A directory containing VHD, VHDX, VDI, or ZFS files.");
            Console.WriteLine("            target directory: Wherte to mount the ZFS file system.");
        }

        static int Main(string[] args)
        {
            try
            {
                return new Program().Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Program crashed horribly:");
                Console.WriteLine(ex);
                return 1;
            }
        }

        int Run(string[] args)
        {
            if (args.Length < 2)
            {
                Usage();
                return 1;
            }

            string zfsDiskDir = args[0];
            string targetDir = args[1];

            if (!(Directory.Exists(zfsDiskDir) || File.Exists(zfsDiskDir)) || !Directory.Exists(targetDir))
            {
                Console.WriteLine("Both directories must already exist.");
                Console.WriteLine();
                Usage();
                return 1;
            }

            if (Compatibility.Status != CompatibilityStatus.Supported)
            {
                Console.WriteLine("ProjectedFS is not supported: " + Compatibility.Status);
                return 1;
            }

            using (mZfs = new Zfs(zfsDiskDir))
            {
                populateDatasets("", mZfs.GetRootDataset());
                using (mProjFs = new ProjectedFileSystem(targetDir, this))
                {
                    Console.WriteLine("Start virtualizing in " + targetDir);
                    Console.WriteLine("Press enter to exit.");
                    Console.ReadLine();
                }
            }

            return 0;
        }

        ProjectedItemInfo populateDatasets(string basePath, DatasetDirectory dsd)
        {
            var rootDir = dsd.GetHeadZfs().Root;
            var info = new ProjectedItemInfo()
            {
                ZfsItem = rootDir,
                ProjectedForm = new FileBasicInfo()
                {
                    IsDirectory = true,
                    Name = dsd.Name,
                },
                FullName = basePath,
            };
            mCache.Add(info.FullName, info);

            var childItems = getChildren(info.FullName, rootDir);

            var childDatasets = dsd.GetChildren();
            foreach (var childDs in childDatasets)
            {
                if (childDs.Value.Type != DataSetType.ZFS)
                    continue;
                childItems.Add(populateDatasets(basePath == "" ? childDs.Key : basePath + "\\" + childDs.Key, childDs.Value));
            }
            info.Children = childItems.ToArray();

            return info;
        }

        List<ProjectedItemInfo> getChildren(string baseName, Zpl.ZfsDirectory dir)
        {
            var projectedChildren = new List<ProjectedItemInfo>();
            foreach (var c in dir.GetChildren())
            {
                var type = c.Type;
                if (type != ZfsItemType.S_IFREG && type != ZfsItemType.S_IFDIR)
                    continue;

                var childInfo = new ProjectedItemInfo();
                childInfo.FullName = baseName == "" ? c.Name : baseName + "\\" + c.Name;
                childInfo.ProjectedForm = new FileBasicInfo()
                {
                    Name = c.Name,
                    IsDirectory = type == ZfsItemType.S_IFDIR,
                    FileSize = type == ZfsItemType.S_IFREG ? ((Zpl.ZfsFile)c).Length : 0,
                    Attributes = FileAttributes.ReadOnly,
                    ChangeTime = c.MTIME,
                    CreationTime = c.CTIME,
                    LastAccessTime = c.ATIME,
                    LastWriteTime = c.MTIME,
                };
                childInfo.ZfsItem = c;
                projectedChildren.Add(childInfo);
                lock (mCache)
                {
                    mCache[childInfo.FullName] = childInfo;
                }
            }
            return projectedChildren;
        }

        public FileBasicInfo[] EnumerateDirectory(bool isWildCardExpression, string directory, string searchExpression)
        {
            var ret = new List<FileBasicInfo>();

            if (isWildCardExpression)
            {
                //TODO
            }
            else
            {
                ProjectedItemInfo info;
                lock (mCache)
                {
                    mCache.TryGetValue(directory, out info);
                }
                if (info != null)
                {
                    if (info.Children == null)
                    {
                        if (info.ZfsItem is Zpl.ZfsDirectory dir)
                        {
                            info.Children = getChildren(info.FullName, dir).ToArray();
                        }
                        else
                        {
                            info.Children = new ProjectedItemInfo[0];
                        }
                    }

                    foreach (var c in info.Children)
                    {
                        if (mProjFs.FileNameMatch(c.FullName, searchExpression))
                            ret.Add(c.ProjectedForm);
                    }
                }
            }
            return ret.ToArray();
        }

        public bool FileExists(string fileName)
        {
            lock (mCache)
            {
                return mCache.ContainsKey(fileName);
            }
        }

        public FileBasicInfo QueryFileInfo(string fileName)
        {
            ProjectedItemInfo ret;
            lock (mCache)
            {
                mCache.TryGetValue(fileName, out ret);
            }
            return ret?.ProjectedForm;
        }

        public bool GetFileData(string fileName, byte[] buf, ulong offset, uint length)
        {
            ProjectedItemInfo ret;
            lock (mCache)
            {
                mCache.TryGetValue(fileName, out ret);
            }
            var file = ret?.ZfsItem as Zpl.ZfsFile;
            if (file == null)
                return false;
            checked
            {
                file.GetContents(new Span<byte>(buf, 0, (int)length), (long)offset);
            }
            return true;
        }
    }
}
