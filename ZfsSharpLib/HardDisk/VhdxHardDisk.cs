using Crc32C;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ZfsSharp.HardDisks
{
    class VhdxHardDisk : HardDisk
    {
        const UInt64 VHDX_SIG = 0x656C696678646876;
        const UInt32 HEADER_SIG = 0x64616568;
        const UInt32 REGION_TABLE_SIG = 0x69676572;
        readonly Guid BAT_REGION_ID = Guid.Parse("2DC27766-F623-4200-9D64-115E9BFD4A08");

        [StructLayout(LayoutKind.Sequential)]
        struct VHDX_HEADER
        {
            public UInt32 Signature;
            public UInt32 Checksum;
            public UInt64 SequenceNumber;
            public Guid FileWriteGuid;
            public Guid DataWriteGuid;
            public Guid LogGuid;
            public UInt16 LogVersion;
            public UInt16 Version;
            public UInt32 LogLength;
            public UInt64 LogOffset;
            //technically the header is 4kb, but we are not including the padding in the struct
            //fixed byte Reserved[4016];
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VHDX_REGION_TABLE_HEADER
        {
            public UInt32 Signature;
            public UInt32 Checksum;
            public UInt32 EntryCount;
            public UInt32 Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VHDX_REGION_TABLE_ENTRY
        {
            public Guid Guid;
            public UInt64 FileOffset;
            public UInt32 Length;
            UInt32 flags;
            public bool Required => (flags & 1) != 0;
        }

        readonly HardDisk mHdd;

        public VhdxHardDisk(HardDisk hdd)
        {
            mHdd = hdd;

            UInt64 sig;
            hdd.Get(0, out sig);
            if (sig != VHDX_SIG)
                throw new Exception("VHDX sig missing!");

            const int HEADER_ALIGNER = 64 * 1024;
            var headers = new List<VHDX_HEADER>();
            MaybeGetHeader(headers, HEADER_ALIGNER);
            MaybeGetHeader(headers, 2 * HEADER_ALIGNER);

            var head = headers.OrderByDescending(h => h.SequenceNumber).First();

            if (head.Version != 1)
                throw new NotSupportedException($"Unsupported VHDX version: {head.Version}");
            if (head.LogGuid != Guid.Empty)
                throw new NotImplementedException("Processing log entries is not supported.");

            //I'm assuming that both region tables are the same, so I just read the first one.
            var regionTableBytes = mHdd.ReadBytes(HEADER_ALIGNER * 3, HEADER_ALIGNER);
            var regionTable = Program.ToStruct<VHDX_REGION_TABLE_HEADER>(regionTableBytes);
            if (regionTable.Signature != REGION_TABLE_SIG)
                throw new Exception("Signature of region table is wrong!");
            for (int i = 0; i < 4; i++)
            {
                regionTableBytes[i + 4] = 0;
            }
            if (regionTable.Checksum != Crc32CAlgorithm.Compute(regionTableBytes))
                throw new Exception("Bad region table checksum!");
            if (regionTable.EntryCount > 2047)
                throw new Exception("Too many region table entries!");

            VHDX_REGION_TABLE_ENTRY? batRegion = null;

            for (int i = 0; i < regionTable.EntryCount; i++)
            {
                int offset = Unsafe.SizeOf<VHDX_REGION_TABLE_HEADER>() + i * Unsafe.SizeOf<VHDX_REGION_TABLE_ENTRY>();
                var entry = Program.ToStruct<VHDX_REGION_TABLE_ENTRY>(regionTableBytes, offset);
                if (entry.Guid == BAT_REGION_ID)
                    batRegion = entry;
            }

            if (!batRegion.HasValue)
                throw new Exception("Could not find BAT region!");

            Console.WriteLine();
        }

        void MaybeGetHeader(List<VHDX_HEADER> headerList, int offset)
        {
            var bytes = mHdd.ReadBytes(offset, 4096);
            if (Program.ToStruct<UInt32>(bytes, 0) != HEADER_SIG)
                return;
            UInt32 checksum = Program.ToStruct<UInt32>(bytes, 4);
            for (int i = 0; i < 4; i++)
            {
                bytes[i + 4] = 0;
            }
            if (checksum != Crc32CAlgorithm.Compute(bytes))
                return;

            headerList.Add(Program.ToStruct<VHDX_HEADER>(bytes));
        }

        public override long Length
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        public override void Get<T>(long offset, out T @struct)
        {
            throw new NotImplementedException();
        }

        public override void ReadBytes(byte[] array, int arrayOffset, long offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
