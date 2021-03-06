﻿using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ZfsSharpLib;

namespace ZfsSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ZfsSharp.exe <a directory containing VHD, VHDX, VDI, or ZFS files>");
                return;
            }

            var sw = Stopwatch.StartNew();
            sw.Stop();

            using (var zfs = new Zfs(args[0]))
            {
                sw = Stopwatch.StartNew();
                foreach (var ds in zfs.GetAllDataSets())
                {
                    Console.WriteLine("{0}: {1}", ds.Type, ds.Name);

                    if (ds.Type != DataSetType.ZFS)
                        continue;

                    var zpl = ds.GetHeadZfs();
                    printContent(ds.Name, zpl.Root);

                    if (ds.Name == "zones/var")
                        Console.WriteLine(Encoding.ASCII.GetString(zpl.GetFileContents(@"/svc/log/svc.startd.log")));

                    foreach (var snap in ds.GetZfsSnapShots())
                    {
                        var snapName = ds.Name + "@" + snap.Key;
                        Console.WriteLine(snapName);
                        printContent(snapName, snap.Value.Root);
                    }
                }
                sw.Stop();
                Console.WriteLine("time: " + sw.ElapsedMilliseconds);
            }

            Console.WriteLine();
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
