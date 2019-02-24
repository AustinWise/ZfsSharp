using Austin.WindowsProjectedFileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using ZfsSharp;

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

            if (!Directory.Exists(zfsDiskDir) || !Directory.Exists(targetDir))
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
                //just use the root dataset for now
                var rootDataset = mZfs.GetRootDataset();
                var rootDir = rootDataset.GetHeadZfs().Root;
                var info = new ProjectedItemInfo()
                {
                    ZfsItem = rootDir,
                    ProjectedForm = new FileBasicInfo()
                    {
                        IsDirectory = true,
                        Name = "",
                    }
                };
                mCache[""] = info;
                using (mProjFs = new ProjectedFileSystem(targetDir, this))
                {
                    Console.WriteLine("Start virtualizing in " + targetDir);
                    Console.WriteLine("Press enter to exit.");
                    Console.ReadLine();
                }
            }

            return 0;
        }

        public FileBasicInfo[] EnumerateDirectory(bool isWildCard, string searchExpression)
        {
            var ret = new List<FileBasicInfo>();
            if (isWildCard)
            {
                //TODO
            }
            else
            {
                ProjectedItemInfo info;
                lock (mCache)
                {
                    mCache.TryGetValue(searchExpression, out info);
                }
                if (info != null)
                {
                    if (info.Children == null)
                    {
                        var projectedChildren = new List<ProjectedItemInfo>();
                        if (info.ZfsItem is Zpl.ZfsDirectory dir)
                        {
                            foreach (var c in dir.GetChildren())
                            {
                                var type = c.Type;
                                if (type != ZfsItemType.S_IFREG && type != ZfsItemType.S_IFDIR)
                                    continue;

                                var childInfo = new ProjectedItemInfo();
                                childInfo.ProjectedForm = new FileBasicInfo()
                                {
                                    Name = c.Name,
                                    IsDirectory = type == ZfsItemType.S_IFDIR,
                                    FileSize = type == ZfsItemType.S_IFREG ? ((Zpl.ZfsFile)c).Length : 0,
                                };
                                childInfo.ZfsItem = c;
                                projectedChildren.Add(childInfo);
                            }
                        }
                        info.Children = projectedChildren.ToArray();
                    }

                    foreach (var c in info.Children)
                    {
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
    }
}
