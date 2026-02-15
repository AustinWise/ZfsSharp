using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZfsSharpLib;

namespace ZfsSharp
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ZfsSharp.exe <one or paths containing VHD, VHDX, VDI, or ZFS files>");
                Console.WriteLine("\tPaths can be files or directories. If a directory is specified, we will search for supported files in that directory and subdirectories.");
                Console.WriteLine("\tSupported disk image format: .vhd, .vhdx, .vdi");
                Console.WriteLine("\tIf a file does not end in that extension, we will treat it as a raw ZFS vdev.");
                return 1;
            }

            var sw = Stopwatch.StartNew();
            sw.Stop();

            var vdevs = new List<LeafVdevInfo>();
            foreach (var path in args)
            {
                if (File.Exists(path))
                {
                    vdevs.Add(LeafVdevInfo.CreateFromFile(new FileInfo(path)));
                }
                else if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path))
                    {
                        vdevs.Add(LeafVdevInfo.CreateFromFile(new FileInfo(file)));
                    }
                }
                else
                {
                    Console.WriteLine($"Path {path} does not exist.");
                    return 1;
                }
            }

            using (var zfs = new Zfs(vdevs))
            {
                sw = Stopwatch.StartNew();
                foreach (var ds in zfs.GetAllDataSets())
                {
                    Console.WriteLine("{0}: {1}", ds.Type, ds.Name);

                    if (ds.Type != DataSetType.ZFS)
                        continue;

                    var zpl = ds.GetHeadZfs();
                    foreach (var kvp in zpl.DataSetExtensions)
                    {
                        Console.WriteLine("\tDataset extension: " + kvp.Key + " = " + kvp.Value);
                    }
                    printContent(ds.Name, zpl.Root);

                    foreach (var snap in ds.GetZfsSnapShots())
                    {
                        var snapName = ds.Name + "@" + snap.Key;
                        Console.WriteLine(snapName);
                        foreach (var kvp in snap.Value.DataSetExtensions)
                        {
                            Console.WriteLine("\tDataset extension: " + kvp.Key + " = " + kvp.Value);
                        }
                        printContent(snapName, snap.Value.Root);
                    }
                }
                sw.Stop();
                Console.WriteLine("time: " + sw.ElapsedMilliseconds);
            }

            Console.WriteLine();
            return 0;
        }

        private static void DumpContents(string outPath, Zpl.ZfsItem item)
        {
            Console.WriteLine(item.FullPath);
            var dir = item as Zpl.ZfsDirectory;
            var file = item as Zpl.ZfsFile;

            var dest = Path.Combine(outPath, item.FullPath.Substring(1));

            if (file != null)
            {
                File.WriteAllBytes(dest, file.GetContents());
            }

            if (dir == null)
                return;
            if (!Directory.Exists(dest))
                Directory.CreateDirectory(dest);
            foreach (var d in dir.GetChildren())
            {
                DumpContents(outPath, d);
            }
        }

        private static void BenchmarkFileReading(Zfs zfs)
        {
            var varzpl = zfs.GetAllDataSets().Where(k => k.Name == "zones/var").Select(ds => ds.GetHeadZfs()).Single();
            Stopwatch st = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                varzpl.GetFileContents(@"/svc/log/svc.startd.log");
            }
            st.Stop();

            Console.WriteLine(st.Elapsed.TotalSeconds);
        }

        static void printContent(string namePrefix, Zpl.ZfsItem item)
        {
            //Console.WriteLine(namePrefix + item.FullPath);
            var dir = item as Zpl.ZfsDirectory;
            var file = item as Zpl.ZfsFile;

            if (file != null)
            {
                int length = (int)file.Length;
                byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
                file.GetContents(new Span<byte>(bytes, 0, length), 0);
                ArrayPool<byte>.Shared.Return(bytes);
            }

            if (dir == null)
                return;
            foreach (var d in dir.GetChildren())
            {
                printContent(namePrefix, d);
            }
        }
    }
}
