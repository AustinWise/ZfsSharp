using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dokan;
using ZfsSharpLib;

namespace ZfsDokan
{
    class Program
    {
        static void Main(string[] args)
        {
            //args = new string[] { @"C:\VPC\SmartOs\" };
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ZfsSharp.exe <a directory containing VHD, VDI, or ZFS files>");
                return;
            }

            using (var zfs = new Zfs(args[0]))
            {
                DokanOptions opt = new DokanOptions();
                opt.MountPoint = "z:\\";
                opt.DebugMode = true;
                opt.UseStdErr = true;
                opt.VolumeLabel = "ZFS";
                int status = DokanNet.DokanMain(opt, new ZfsDokan(zfs));
                switch (status)
                {
                    case DokanNet.DOKAN_DRIVE_LETTER_ERROR:
                        Console.WriteLine("Drvie letter error");
                        break;
                    case DokanNet.DOKAN_DRIVER_INSTALL_ERROR:
                        Console.WriteLine("Driver install error");
                        break;
                    case DokanNet.DOKAN_MOUNT_ERROR:
                        Console.WriteLine("Mount error");
                        break;
                    case DokanNet.DOKAN_START_ERROR:
                        Console.WriteLine("Start error");
                        break;
                    case DokanNet.DOKAN_ERROR:
                        Console.WriteLine("Unknown error");
                        break;
                    case DokanNet.DOKAN_SUCCESS:
                        Console.WriteLine("Success");
                        break;
                    default:
                        Console.WriteLine("Unknown status: {0}", status);
                        break;

                }
            }
        }
    }
}
