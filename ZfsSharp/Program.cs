using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZfsSharpLib;
using System.Text;
using System.Globalization;
using System.Buffers;

namespace ZfsSharp
{
    static class Program
    {
        private static void printDva(dva_t dva)
        {
            Console.WriteLine($"\t\tis gang block: {dva.IsGang}");
            Console.WriteLine($"\t\tVDEV: {dva.VDev}");
            Console.WriteLine($"\t\tASIZE: {dva.ASize}");
            Console.WriteLine($"\t\toffset: {dva.Offset}");
        }

        private static void printBlockPointer(blkptr_t blkptr)
        {
            Console.WriteLine("Block pointer type: " + (blkptr.IsEmbedded ? "embedded data" : "normal"));
            Console.WriteLine($"logical birth txg: {blkptr.birth}");
            if (!blkptr.IsEmbedded)
            {
                Console.WriteLine($"physical birth: {blkptr.PhysBirth}");
                Console.WriteLine($"fill: {blkptr.fill}");
            }
            Console.WriteLine($"logical size (bytes): {blkptr.LogicalSizeBytes}");
            Console.WriteLine($"physical size (bytes): {blkptr.PhysicalSizeBytes}");
            Console.WriteLine("Properties:");
            Console.WriteLine($"\tis little endian: {blkptr.IsLittleEndian}");
            Console.WriteLine($"\tis dedup: {blkptr.IsDedup}");
            Console.WriteLine($"\tlevel: {blkptr.Level}");
            Console.WriteLine($"\ttype: {blkptr.Type}");
            Console.WriteLine($"\tcompression: {blkptr.Compress}");
            Console.WriteLine($"\tcompression: {blkptr.Compress}");


            if (blkptr.IsEmbedded)
            {
                Console.WriteLine($"\tembedded data type: {blkptr.EmbedType}");
                Console.WriteLine($"\tPSIZE: {blkptr.EmbedProps.PSize}");
                Console.WriteLine($"\tLSIZE: {blkptr.EmbedProps.LSize}");
                Console.WriteLine("Embedded data:");
                var data = new byte[blkptr.LogicalSizeBytes];
                Zio.ReadEmbedded(blkptr, data);
                Console.WriteLine("\t" + string.Join(" ", data.Select(b => b.ToString("x"))));
            }
            else
            {
                Console.WriteLine($"\tPSIZE: {blkptr.NormalProps.PSize}");
                Console.WriteLine($"\tLSIZE: {blkptr.NormalProps.LSize}");
                Console.WriteLine($"\tchecksum type: {blkptr.Checksum}");

                Console.WriteLine("Data:");
                Console.WriteLine("\tDVA 1:");
                printDva(blkptr.dva1);
                Console.WriteLine("\tDVA 2:");
                printDva(blkptr.dva2);
                Console.WriteLine("\tDVA 3:");
                printDva(blkptr.dva3);
            }
        }

        static void Main(string[] args)
        {
            const string STUFF = @"";

            var bytes = STUFF.Split(new char[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(ch => byte.Parse(ch, NumberStyles.HexNumber)).ToArray();
            Debug.Assert(bytes.Length == 128);

            var blkptr = ZfsSharpLib.Program.ToStruct<blkptr_t>(bytes);
            printBlockPointer(blkptr);

            return;

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
