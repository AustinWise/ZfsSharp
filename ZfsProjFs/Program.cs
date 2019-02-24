using Austin.WindowsProjectedFileSystem;
using System;
using System.IO;
using ZfsSharp;

namespace ZfsProjFs
{
    class Program : IProjectedFileSystemCallbacks
    {
        Zfs mZfs;

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
                using (var projFs = new ProjectedFileSystem(targetDir, this))
                {
                    Console.WriteLine("Start virtualizing in " + targetDir);
                    Console.WriteLine("Press enter to exit.");
                    Console.ReadLine();
                }
            }

            return 0;
        }
    }
}
