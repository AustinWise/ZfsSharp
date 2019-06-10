using Crc32C;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ZfsSharpLib.HardDisks
{
    class VhdxHardDisk : OffsetTableHardDisk
    {
        #region Structs and stuff
        const int MAX_TABLE_ENTRIES = 2047; //region table and metadata table
        const int HEADER_SIZE = 64 * 1024;
        const int METADATA_TABLE_SIZE = 64 * 1024;
        const UInt64 VHDX_SIG = 0x656C696678646876;
        const UInt32 HEADER_SIG = 0x64616568;
        const UInt32 REGION_TABLE_SIG = 0x69676572;
        const UInt64 METADATA_TABLE_SIG = 0x617461646174656D;
        static readonly Guid REGION_BAT = Guid.Parse("2DC27766-F623-4200-9D64-115E9BFD4A08");
        static readonly Guid REGION_METADATA = Guid.Parse("8B7CA206-4790-4B9A-B8FE-575F050F886E");
        static readonly Guid METADATA_FILE_PARAMS = Guid.Parse("CAA16737-FA36-4D43-B3B6-33F0AA44E76B");
        static readonly Guid METADATA_VIRTUAL_DISK_SIZE = Guid.Parse("2FA54224-CD1B-4876-B211-5DBED83BF4B8");
        static readonly Guid METADATA_PAGE_83_DATA = Guid.Parse("BECA12AB-B2E6-4523-93EF-C309E000C746");
        static readonly Guid METADATA_LOGICAL_SECTOR_SIZE = Guid.Parse("8141BF1D-A96F-4709-BA47-F233A8FAAB5F");
        static readonly Guid METADATA_PHSYICAL_SECTOR_SIZE = Guid.Parse("CDA348C7-445D-4471-9CC9-E9885251C556");
        static readonly Guid METADATA_PARENT_LOCATOR = Guid.Parse("A8D35F2D-B30B-454D-ABF7-D3D84834AB0C");
        static readonly HashSet<Guid> sKnownMetadataItems = new HashSet<Guid>(new Guid[]
        {
            METADATA_FILE_PARAMS,
            METADATA_VIRTUAL_DISK_SIZE,
            METADATA_PAGE_83_DATA,
            METADATA_LOGICAL_SECTOR_SIZE,
            METADATA_PHSYICAL_SECTOR_SIZE,
            METADATA_PARENT_LOCATOR,
        });

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

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct VHDX_METADATA_TABLE_HEADER
        {
            public UInt64 Signature;
            UInt16 Reserved;
            public UInt16 EntryCount;
            fixed UInt32 Reserved2[5];
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VHDX_METADATA_TABLE_ENTRY
        {
            public Guid ItemId;
            public UInt32 Offset;
            public UInt32 Length;
            UInt32 flags;
            UInt32 Reserved2;

            public bool IsUser => (flags & 0x1) != 0;
            public bool IsVirtualDisk => (flags & 0x2) != 0;
            public bool IsRequired => (flags & 0x4) != 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VHDX_FILE_PARAMETERS
        {
            public UInt32 BlockSize;
            UInt32 flags;
            public bool LeaveBlocksAllocated => (flags & 0x1) != 0;
            public bool HasParent => (flags & 0x2) != 0;
        }

        enum PayloadBlockState
        {
            NotPresent = 0,
            Undefined = 1,
            BlockZero = 2,
            Unmapped = 3,
            FullyPresent = 6,
            PartiallyPresent = 7,
        }

        enum SectorBitmapState
        {
            NotPresent = 0,
            Present = 6,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VHDX_BAT_ENTRY
        {
            const int FileOffsetMask = (1 << 44) - 1;
            UInt64 data;
            public PayloadBlockState PayloadState => (PayloadBlockState)(data & 0x7);
            public SectorBitmapState SectorBitmapState => (SectorBitmapState)(data & 0x7);
            public Int64 FileOffsetMB => (long)((data >> 20) & FileOffsetMask);
        }
        #endregion

        readonly long mVirtualDiskSize;

        public VhdxHardDisk(HardDisk hdd)
            : base(hdd)
        {
            VHDX_REGION_TABLE_ENTRY batRegion, metadataRegion;
            GetRegions(out batRegion, out metadataRegion);

            VHDX_FILE_PARAMETERS fileParams;
            uint logicalSectorSize;
            ReadMetadata(metadataRegion, out fileParams, out mVirtualDiskSize, out logicalSectorSize);

            if (fileParams.HasParent)
                throw new NotImplementedException("Differencing disk are not supported.");

            //check all these calculations to make sure our assumptions about data sizes are correct
            checked
            {
                mBlockSize = (int)fileParams.BlockSize;

                int chunkRatio = (int)((1L << 23) * logicalSectorSize / mBlockSize);
                int dataBlockCount = (int)Math.Ceiling((decimal)mVirtualDiskSize / mBlockSize);
                int sectorBitmapCount = (int)Math.Ceiling((decimal)dataBlockCount / chunkRatio);
                int totalBatEntries = dataBlockCount + (int)Math.Floor((dataBlockCount - 1) / (decimal)chunkRatio);

                if (batRegion.Length < Program.SizeOf<VHDX_BAT_ENTRY>() * totalBatEntries)
                    throw new Exception("Bat region is not big enough to contain all the bat entries!");

                var batBytes = mHdd.ReadBytes((long)batRegion.FileOffset, (int)batRegion.Length);

                mBlockOffsets = new long[dataBlockCount];
                int fileOffsetsNdx = 0;
                for (int i = 0; i < totalBatEntries; i++)
                {
                    var isSectorBitmap = i % (chunkRatio + 1) == chunkRatio;
                    var entry = Program.ToStruct<VHDX_BAT_ENTRY>(batBytes, i * Program.SizeOf<VHDX_BAT_ENTRY>());
                    if (isSectorBitmap)
                    {
                        if (entry.SectorBitmapState != SectorBitmapState.NotPresent)
                            throw new Exception("Present bat entry!");
                    }
                    else
                    {
                        long offset;
                        switch (entry.PayloadState)
                        {
                            case PayloadBlockState.NotPresent:
                            case PayloadBlockState.Undefined:
                            case PayloadBlockState.BlockZero:
                            case PayloadBlockState.Unmapped:
                                offset = 0;
                                break;
                            case PayloadBlockState.FullyPresent:
                                offset = entry.FileOffsetMB << 20;
                                break;
                            case PayloadBlockState.PartiallyPresent:
                                throw new NotSupportedException("Partially present blocks are not supported.");
                            default:
                                throw new Exception($"Unknown BAT entry state: {entry.PayloadState}");
                        }
                        if (offset == 0)
                            offset = -1;
                        mBlockOffsets[fileOffsetsNdx++] = offset;
                    }
                }
            }
        }

        private void ReadMetadata(VHDX_REGION_TABLE_ENTRY metadataRegion, out VHDX_FILE_PARAMETERS outFileParams, out Int64 outVirtualDiskSize, out uint outLogicalSectorSize)
        {
            if (metadataRegion.Length < METADATA_TABLE_SIZE)
                throw new Exception("Metadata region is too small to contain metadata table!");
            var metadataBytes = mHdd.ReadBytes(checked((long)metadataRegion.FileOffset), checked((int)metadataRegion.Length));

            var metadataHeader = Program.ToStruct<VHDX_METADATA_TABLE_HEADER>(metadataBytes, 0);
            if (metadataHeader.Signature != METADATA_TABLE_SIG)
                throw new Exception("Bad metadata header sig.");
            if (metadataHeader.EntryCount > MAX_TABLE_ENTRIES)
                throw new Exception("Too many metadata entries.");
            VHDX_FILE_PARAMETERS? fileParams = null;
            UInt64? virtualDiskSize = null;
            UInt32? logicalSectorSize = null;
            for (int i = 0; i < metadataHeader.EntryCount; i++)
            {
                int readOffset = Program.SizeOf<VHDX_METADATA_TABLE_HEADER>()
                                 + i * Program.SizeOf<VHDX_METADATA_TABLE_ENTRY>();
                var entry = Program.ToStruct<VHDX_METADATA_TABLE_ENTRY>(metadataBytes, readOffset);

                if (entry.IsUser)
                {
                    if (entry.IsRequired)
                        throw new Exception($"Unknown required metadata item: {entry.ItemId}");
                    else
                        continue;
                }
                else if (entry.IsRequired && !sKnownMetadataItems.Contains(entry.ItemId))
                    throw new Exception($"Unknown required metadata item: {entry.ItemId}");

                //the ArraySegment overload of ToStruct will make sure the entry size matches the data item
                var entryBytes = new ArraySegment<byte>(metadataBytes, checked((int)entry.Offset), checked((int)entry.Length));
                if (entry.ItemId == METADATA_FILE_PARAMS)
                {
                    fileParams = Program.ToStruct<VHDX_FILE_PARAMETERS>(entryBytes);
                }
                else if (entry.ItemId == METADATA_VIRTUAL_DISK_SIZE)
                {
                    virtualDiskSize = Program.ToStruct<UInt64>(entryBytes);
                }
                else if (entry.ItemId == METADATA_LOGICAL_SECTOR_SIZE)
                {
                    logicalSectorSize = Program.ToStruct<UInt32>(entryBytes);
                }
            }

            if (!fileParams.HasValue || !virtualDiskSize.HasValue || !logicalSectorSize.HasValue)
                throw new Exception("Missing required metadata!");

            outFileParams = fileParams.Value;
            outVirtualDiskSize = checked((long)virtualDiskSize.Value);
            outLogicalSectorSize = logicalSectorSize.Value;
        }

        private void GetRegions(out VHDX_REGION_TABLE_ENTRY outBatRegion, out VHDX_REGION_TABLE_ENTRY outMetadataRegion)
        {
            UInt64 sig;
            mHdd.Get(0, out sig);
            if (sig != VHDX_SIG)
                throw new Exception("VHDX sig missing!");

            var headers = new List<VHDX_HEADER>();
            MaybeGetHeader(headers, HEADER_SIZE);
            MaybeGetHeader(headers, 2 * HEADER_SIZE);

            var head = headers.OrderByDescending(h => h.SequenceNumber).First();

            if (head.Version != 1)
                throw new NotSupportedException($"Unsupported VHDX version: {head.Version}");
            if (head.LogGuid != Guid.Empty)
                throw new NotImplementedException("Processing log entries is not supported.");

            //I'm assuming that both region tables are the same, so I just read the first one.
            var regionTableBytes = mHdd.ReadBytes(HEADER_SIZE * 3, HEADER_SIZE);
            var regionTable = Program.ToStruct<VHDX_REGION_TABLE_HEADER>(regionTableBytes);
            if (regionTable.Signature != REGION_TABLE_SIG)
                throw new Exception("Signature of region table is wrong!");
            for (int i = 0; i < 4; i++)
            {
                regionTableBytes[i + 4] = 0;
            }
            if (regionTable.Checksum != CRC.Compute(regionTableBytes))
                throw new Exception("Bad region table checksum!");
            if (regionTable.EntryCount > 2047)
                throw new Exception("Too many region table entries!");

            VHDX_REGION_TABLE_ENTRY? batRegion = null, metadataRegion = null;
            for (int i = 0; i < regionTable.EntryCount; i++)
            {
                int offset = Program.SizeOf<VHDX_REGION_TABLE_HEADER>() + i * Program.SizeOf<VHDX_REGION_TABLE_ENTRY>();
                var entry = Program.ToStruct<VHDX_REGION_TABLE_ENTRY>(regionTableBytes, offset);

                if (entry.Guid == REGION_BAT)
                    batRegion = entry;
                else if (entry.Guid == REGION_METADATA)
                    metadataRegion = entry;
                else if (entry.Required)
                    throw new Exception($"Unknown region: {entry.Guid}");
            }

            if (!batRegion.HasValue)
                throw new Exception("Could not find BAT region!");
            if (!metadataRegion.HasValue)
                throw new Exception("Could not find metadata region!");

            outBatRegion = batRegion.Value;
            outMetadataRegion = metadataRegion.Value;
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
            if (checksum != CRC.Compute(bytes))
                return;

            headerList.Add(Program.ToStruct<VHDX_HEADER>(bytes));
        }

        public override long Length => mVirtualDiskSize;
    }
}
