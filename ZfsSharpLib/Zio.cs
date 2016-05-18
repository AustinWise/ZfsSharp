using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ZfsSharp.VirtualDevices;

namespace ZfsSharp
{
    class Zio
    {
        static readonly Sha256 sEmbeddedChecksum = new Sha256();

        public unsafe static bool IsEmbeddedChecksumValid(byte[] bytes, zio_cksum_t verifier)
        {
            int embeddedChecksumSize = sizeof(zio_eck_t);
            if (bytes.Length <= embeddedChecksumSize)
                throw new ArgumentOutOfRangeException(nameof(bytes), "Not enough space for an embedded checksum.");

            fixed (byte* bytePtr = bytes)
            {
                zio_eck_t* pzec = (zio_eck_t*)(bytePtr + bytes.Length - embeddedChecksumSize);
                if (!pzec->IsMagicValid)
                    return false;
                var expectedChecksum = pzec->zec_cksum;
                pzec->zec_cksum = verifier;
                var actualChecksum = sEmbeddedChecksum.Calculate(new ArraySegment<byte>(bytes));
                if (!actualChecksum.Equals(expectedChecksum))
                    return false;
                pzec->zec_cksum = expectedChecksum;
                return true;
            }
        }

        const int SPA_MINBLOCKSHIFT = 9;
        public const int SPA_MINBLOCKSIZE = 1 << SPA_MINBLOCKSHIFT; //512 bytes. ASIZE, LSIZE, and PSIZE are multiples of this.

        private Vdev[] mVdevs;
        //TODO: change these not to use a dictionary so lookup is faster
        private Dictionary<zio_checksum, IChecksum> mChecksums = new Dictionary<zio_checksum, IChecksum>();
        private Dictionary<zio_compress, ICompression> mCompression = new Dictionary<zio_compress, ICompression>();

        public Zio(Vdev[] vdevs)
        {
            mVdevs = vdevs;

            mChecksums.Add(zio_checksum.OFF, new NoChecksum());
            mChecksums.Add(zio_checksum.FLETCHER_4, new Flecter4());
            mChecksums.Add(zio_checksum.SHA256, new Sha256());

            mCompression.Add(zio_compress.LZJB, new Lzjb());
            mCompression.Add(zio_compress.OFF, new NoCompression());
            mCompression.Add(zio_compress.LZ4, new LZ4());

            var gz = new GZip();
            for (int i = (int)zio_compress.GZIP_1; i <= (int)zio_compress.GZIP_9; i++)
            {
                mCompression[(zio_compress)i] = gz;
            }
        }

        void LogChecksumError()
        {
            //TODO: raise an event or something
        }

        //a bit of a layering violation
        public void InitMetaSlabs(ObjectSet mos)
        {
            foreach (var v in mVdevs)
            {
                v.InitMetaSlabs(mos);
            }
        }

        unsafe void ReadEmbedded(blkptr_t blkptr, ArraySegment<byte> dest)
        {
            if (blkptr.EmbedType != EmbeddedType.Data)
                throw new Exception("Unsupported embedded type: " + blkptr.EmbedType);

            int physicalSize = blkptr.PSize + 1;
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
                int remaingBytes = physicalSize;

                for (int i = 0; remaingBytes > 0 && i < NUMBER_OF_EMBEDDED_CHUNKS; i++)
                {
                    int size = Math.Min(remaingBytes, blkptr_t.EmbeddedSizes[i]);
                    Debug.Assert(size > 0);
                    Unsafe.CopyBlock(pBytes, embeddedDataPoints[i], (uint)size);
                    pBytes += size;
                    remaingBytes -= size;
                }
            }

            mCompression[blkptr.Compress].Decompress(physicalBytes, dest);
            Program.ReturnBytes(physicalBytes);
        }

        public void Read(blkptr_t blkptr, ArraySegment<byte> dest)
        {
            if (blkptr.birth == 0)
                throw new NotSupportedException("Invalid block pointer: 0 birth txg.");
            if (blkptr.IsHole)
                throw new Exception("Block pointer is a hole.");
            if (blkptr.IsLittleEndian != BitConverter.IsLittleEndian)
                throw new NotImplementedException("Byte swapping not implemented.");
            if (blkptr.LogicalSizeBytes != dest.Count)
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

        private static zio_cksum_t CalculateGangChecksumVerifier(ref blkptr_t blkptr)
        {
            var ret = new zio_cksum_t();
            ret.word1 = (ulong)blkptr.dva1.VDev;
            ret.word2 = (ulong)(blkptr.dva1.Offset << SPA_MINBLOCKSHIFT);
            ret.word3 = (ulong)blkptr.PhysBirth;
            ret.word4 = 0;
            return ret;
        }

        private void ReadGangBlkPtr(blkptr_t blkptr, ArraySegment<byte> dest, ref int offset)
        {
            int size = blkptr.LogicalSizeBytes;
            Read(blkptr, dest.SubSegment(offset, size));
            offset += size;
        }

        private void Read(blkptr_t blkptr, dva_t dva, ArraySegment<byte> dest)
        {
            if (dva.IsGang && blkptr.Compress != zio_compress.OFF)
            {
                throw new Exception("A compressed gang block? It seems redundent to decompress twice.");
            }

            Vdev dev = mVdevs[dva.VDev];
            int hddReadSize = dva.IsGang ? zio_gbh_phys_t.SPA_GANGBLOCKSIZE : ((int)blkptr.PSize + 1) * SPA_MINBLOCKSIZE;

            foreach (byte[] hddBytes in dev.ReadBytes(dva.Offset << SPA_MINBLOCKSHIFT, hddReadSize))
            {
                byte[] physicalBytes;
                if (dva.IsGang)
                {
                    var gangHeader = Program.ToStruct<zio_gbh_phys_t>(hddBytes);
                    if (!IsEmbeddedChecksumValid(hddBytes, CalculateGangChecksumVerifier(ref blkptr)))
                        continue;

                    physicalBytes = new byte[
                        gangHeader.zg_blkptr1.LogicalSizeBytes +
                        gangHeader.zg_blkptr2.LogicalSizeBytes +
                        gangHeader.zg_blkptr3.LogicalSizeBytes];

                    int offset = 0;
                    ReadGangBlkPtr(gangHeader.zg_blkptr1, new ArraySegment<byte>(physicalBytes), ref offset);
                    ReadGangBlkPtr(gangHeader.zg_blkptr2, new ArraySegment<byte>(physicalBytes), ref offset);
                    ReadGangBlkPtr(gangHeader.zg_blkptr3, new ArraySegment<byte>(physicalBytes), ref offset);

                    if (offset != physicalBytes.Length)
                        throw new Exception("Did not read enough gang data!");
                }
                else
                {
                    physicalBytes = hddBytes;
                }

                var chk = mChecksums[blkptr.Checksum].Calculate(new ArraySegment<byte>(physicalBytes));
                if (!chk.Equals(blkptr.cksum))
                {
                    LogChecksumError();
                    continue;
                }

                mCompression[blkptr.Compress].Decompress(new ArraySegment<byte>(physicalBytes), dest);
                return;
            }

            throw new Exception("Could not find a correct copy of the requested data.");
        }

        public unsafe T Get<T>(blkptr_t blkptr) where T : struct
        {
            byte[] bytes = new byte[blkptr.LogicalSizeBytes];
            Read(blkptr, new ArraySegment<byte>(bytes));
            return Program.ToStruct<T>(bytes);
        }
    }

    interface IChecksum
    {
        zio_cksum_t Calculate(ArraySegment<byte> input);
    }

    interface ICompression
    {
        void Decompress(ArraySegment<byte> input, ArraySegment<byte> output);
    }
}
