#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZfsSharpLib.VirtualDevices;

namespace ZfsSharpLib
{
    class Zio
    {
        static readonly Sha256 sEmbeddedChecksum = new Sha256();

        public unsafe static bool IsEmbeddedChecksumValid(ReadOnlySpan<byte> bytesToVerify, zio_cksum_t verifier)
        {
            int embeddedChecksumContainerSize = sizeof(zio_eck_t);
            if (bytesToVerify.Length <= embeddedChecksumContainerSize)
                throw new ArgumentOutOfRangeException(nameof(bytesToVerify), "Not enough space for an embedded checksum.");

            zio_eck_t embeddedChecksumContainer = Program.ToStruct<zio_eck_t>(bytesToVerify.Slice(bytesToVerify.Length - embeddedChecksumContainerSize));

            if (!embeddedChecksumContainer.IsMagicValid)
                return false;

            var expectedChecksum = embeddedChecksumContainer.zec_cksum;
            int checksumSize = sizeof(zio_cksum_t);
            // TODO: do this without copying all of the bytes somehow.
            byte[] copy = new byte[bytesToVerify.Length];
            bytesToVerify.Slice(0, bytesToVerify.Length - checksumSize).CopyTo(copy);
            MemoryMarshal.AsBytes(new ReadOnlySpan<zio_cksum_t>(ref verifier)).CopyTo(copy.AsSpan(copy.Length - checksumSize));

            var actualChecksum = sEmbeddedChecksum.Calculate(copy);
            return actualChecksum.Equals(expectedChecksum);
        }

        const int SPA_MINBLOCKSHIFT = 9;
        public const int SPA_MINBLOCKSIZE = 1 << SPA_MINBLOCKSHIFT; //512 bytes. ASIZE, LSIZE, and PSIZE are multiples of this.

        private readonly Vdev[] mVdevs;
        private readonly IChecksum?[] mChecksums;
        private readonly ICompression?[] mCompression;

        public Zio(Vdev[] vdevs)
        {
            mVdevs = vdevs;
            mChecksums = new IChecksum[(int)zio_checksum.FUNCTIONS];
            mCompression = new ICompression[(int)zio_compress.FUNCTIONS];

            mChecksums[(int)zio_checksum.OFF] = new NoChecksum();
            mChecksums[(int)zio_checksum.FLETCHER_4] = new Flecter4();
            mChecksums[(int)zio_checksum.SHA256] = new Sha256();

            mCompression[(int)zio_compress.LZJB] = new Lzjb();
            mCompression[(int)zio_compress.OFF] = new NoCompression();
            mCompression[(int)zio_compress.LZ4] = new LZ4();
            mCompression[(int)zio_compress.ZSTD] = new ZStd();

            var gz = new GZip();
            for (int i = (int)zio_compress.GZIP_1; i <= (int)zio_compress.GZIP_9; i++)
            {
                mCompression[i] = gz;
            }
        }

        private IChecksum GetChecksum(in blkptr_t blkptr)
        {
            int ndx = (byte)blkptr.Checksum;
            if (ndx > mChecksums.Length)
                throw new Exception("Checksum type out of range: " + blkptr.Checksum);
            IChecksum? checksumAlgo = mChecksums[ndx];
            if (checksumAlgo is null)
                throw new NotImplementedException("Unimplemented checksum algorithm: " + blkptr.Checksum);
            return checksumAlgo;
        }

        private ICompression GetCompression(in blkptr_t blkptr)
        {
            int ndx = (byte)blkptr.Compress;
            if (ndx > mCompression.Length)
                throw new Exception("Compression type out of range: " + blkptr.Compress);
            ICompression? checksumAlgo = mCompression[ndx];
            if (checksumAlgo is null)
                throw new NotImplementedException("Unimplemented compression algorithm: " + blkptr.Compress);
            return checksumAlgo;
        }

        //a bit of a layering violation
        public void InitMetaSlabs(ObjectSet mos)
        {
            foreach (var v in mVdevs)
            {
                v.InitMetaSlabs(mos);
            }
        }

        unsafe void ReadEmbedded(blkptr_t blkptr, Span<byte> dest)
        {
            if (blkptr.EmbedType != EmbeddedType.Data)
                throw new Exception("Unsupported embedded type: " + blkptr.EmbedType);

            int physicalSize = blkptr.PhysicalSizeBytes;
            if (physicalSize > blkptr_t.EM_DATA_SIZE)
                throw new Exception("PSize is too big!");
            var physicalBytes = Program.RentBytes(physicalSize);

            const int NUMBER_OF_EMBEDDED_CHUNKS = 3;

            Debug.Assert(blkptr_t.EmbeddedSizes.Length == NUMBER_OF_EMBEDDED_CHUNKS);
            byte** embeddedDataPoints = stackalloc byte*[NUMBER_OF_EMBEDDED_CHUNKS];
            embeddedDataPoints[0] = blkptr.EmbeddedData1;
            embeddedDataPoints[1] = blkptr.EmbeddedData2;
            embeddedDataPoints[2] = blkptr.EmbeddedData3;

            fixed (byte* pStartPtr = physicalBytes.Array)
            {
                var pBytes = pStartPtr + physicalBytes.Offset;
                int remainingBytes = physicalSize;

                for (int i = 0; remainingBytes > 0 && i < NUMBER_OF_EMBEDDED_CHUNKS; i++)
                {
                    int size = Math.Min(remainingBytes, blkptr_t.EmbeddedSizes[i]);
                    Debug.Assert(size > 0);
                    Unsafe.CopyBlock(pBytes, embeddedDataPoints[i], (uint)size);
                    pBytes += size;
                    remainingBytes -= size;
                }
            }

            ICompression comp = GetCompression(in blkptr);
            comp.Decompress(physicalBytes, dest);
            Program.ReturnBytes(physicalBytes);
        }

        public void Read(blkptr_t blkptr, Span<byte> dest)
        {
            if (blkptr.birth == 0)
                throw new NotSupportedException("Invalid block pointer: 0 birth txg.");
            if (blkptr.IsHole)
                throw new Exception("Block pointer is a hole.");
            if (blkptr.IsLittleEndian != BitConverter.IsLittleEndian)
                throw new NotImplementedException("Byte swapping not implemented.");
            if (blkptr.LogicalSizeBytes != dest.Length)
                throw new ArgumentOutOfRangeException("dest", "Dest does not match logical size of block pointer.");

            if (blkptr.IsEmbedded)
            {
                ReadEmbedded(blkptr, dest);
                return;
            }

            //if (blkptr.fill == 0)
            //    throw new NotSupportedException("There is no data in this block pointer.");

            //TODO: try other DVAs
            Read(blkptr, blkptr.dva1, dest);
        }

        public byte[] ReadBytes(blkptr_t blkptr)
        {
            var bytes = new byte[blkptr.LogicalSizeBytes];
            Read(blkptr, bytes);
            return bytes;
        }

        private static zio_cksum_t CalculateGangChecksumVerifier(ref blkptr_t blkptr)
        {
            var ret = new zio_cksum_t();
            ret.word1 = (ulong)blkptr.dva1.VDev;
            ret.word2 = (ulong)(blkptr.dva1.Offset << SPA_MINBLOCKSHIFT);
            ret.word3 = (ulong)blkptr.PhysBirth;
            ret.word4 = 0;
            return ret;
        }

        private void ReadGangBlkPtr(blkptr_t blkptr, Span<byte> dest, ref int offset)
        {
            int size = blkptr.LogicalSizeBytes;
            Read(blkptr, dest.Slice(offset, size));
            offset += size;
        }

        /// <summary>
        /// Just reads the bytes off the disk, reading gang blocks as needed.
        /// </summary>
        /// <param name="blkptr"></param>
        /// <param name="dva"></param>
        /// <returns>A buffer allocated from <see cref="Program.RentBytes"/>.</returns>
        private ArraySegment<byte> ReadPhysical(blkptr_t blkptr, dva_t dva)
        {
            Vdev dev = mVdevs[dva.VDev];
            int hddReadSize = dva.IsGang ? zio_gbh_phys_t.SPA_GANGBLOCKSIZE : blkptr.PhysicalSizeBytes;

            var hddBytes = Program.RentBytes(hddReadSize);
            dev.ReadBytes(hddBytes, dva.Offset << SPA_MINBLOCKSHIFT);

            if (!dva.IsGang)
            {
                return hddBytes;
            }

            var gangHeader = Program.ToStruct<zio_gbh_phys_t>(hddBytes);

            bool isChecksumValid = IsEmbeddedChecksumValid(hddBytes, CalculateGangChecksumVerifier(ref blkptr));

            Program.ReturnBytes(hddBytes);
            hddBytes = default(ArraySegment<byte>);

            if (!isChecksumValid)
                throw new Exception("Could not find a correct copy of the requested data.");

            int realPhysicalSize =
                gangHeader.zg_blkptr1.LogicalSizeBytes +
                gangHeader.zg_blkptr2.LogicalSizeBytes +
                gangHeader.zg_blkptr3.LogicalSizeBytes;
            Debug.Assert(realPhysicalSize == blkptr.PhysicalSizeBytes);
            var physicalBytes = Program.RentBytes(realPhysicalSize);

            int offset = 0;
            ReadGangBlkPtr(gangHeader.zg_blkptr1, physicalBytes, ref offset);
            ReadGangBlkPtr(gangHeader.zg_blkptr2, physicalBytes, ref offset);
            ReadGangBlkPtr(gangHeader.zg_blkptr3, physicalBytes, ref offset);
            Debug.Assert(offset == realPhysicalSize, "Did not read enough gang data!");

            return physicalBytes;
        }

        private void Read(blkptr_t blkptr, dva_t dva, Span<byte> dest)
        {
            if (dva.IsGang && blkptr.Compress != zio_compress.OFF)
            {
                throw new Exception("A compressed gang block? It seems redundant to decompress twice.");
            }

            var physicalBytes = ReadPhysical(blkptr, dva);
            IChecksum checksumAlgo = GetChecksum(in blkptr);
            var chk = checksumAlgo.Calculate(physicalBytes);
            if (!chk.Equals(blkptr.cksum))
            {
                throw new Exception("Could not find a correct copy of the requested data.");
            }

            ICompression compressionAlgo = GetCompression(in blkptr);
            compressionAlgo.Decompress(physicalBytes, dest);
            Program.ReturnBytes(physicalBytes);
        }

        public unsafe T Get<T>(blkptr_t blkptr) where T : struct
        {
            var bytes = Program.RentBytes(blkptr.LogicalSizeBytes);
            try
            {
                Read(blkptr, bytes);
                return Program.ToStruct<T>(bytes);
            }
            finally
            {
                Program.ReturnBytes(bytes);
            }
        }
    }

    interface IChecksum
    {
        zio_cksum_t Calculate(ReadOnlySpan<byte> input);
    }

    interface ICompression
    {
        void Decompress(ReadOnlySpan<byte> input, Span<byte> output);
    }
}
