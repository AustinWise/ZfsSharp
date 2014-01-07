using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp.VirtualDevices
{
    class RaidzVdev : Vdev
    {
        readonly Vdev[] mVdevs;
        readonly ulong mNparity;
        readonly int mUnitShift; //ashift
        public RaidzVdev(NvList config, Dictionary<ulong, LeafVdevInfo> leafs)
            : base(config)
        {
            this.mVdevs = config.Get<NvList[]>("children")
                .Select(child => Vdev.Create(child, leafs))
                .OrderBy(child => child.ID)
                .ToArray();

            mNparity = config.Get<UInt64>("nparity");
            mUnitShift = (int)config.Get<UInt64>("ashift");
        }

        public override IEnumerable<byte[]> ReadBytes(long offset, long count)
        {
            var rm = vdev_raidz_map_alloc((ulong)count, (ulong)offset, mUnitShift, (ulong)mVdevs.Length, mNparity);
            var ret = new byte[count];
            long ptr = 0;
            for (ulong i = rm.rm_firstdatacol; i < rm.rm_cols; i++)
            {
                var col = rm.rm_col[i];
                var data = mVdevs[col.rc_devidx].ReadBytes((long)col.rc_offset, (long)col.rc_size).First();
                Program.LongBlockCopy(data, 0, ret, ptr, (long)col.rc_size);
                ptr += (long)col.rc_size;
            }

            yield return ret;
        }

        //All below is copy-pasted with some modification from usr\src\uts\common\fs\zfs\vdev_raidz.c
        //Inspired by http://www.joyent.com/blog/zfs-raidz-striping

        [StructLayout(LayoutKind.Sequential)]
        struct raidz_col
        {
            public ulong rc_devidx;		/* child device index for I/O */
            public ulong rc_offset;		/* device offset */
            public ulong rc_size;		/* I/O size */
            //public void* rc_data;			/* I/O data */
            //public void* rc_gdata;			/* used to store the "good" version */
            public int rc_error;			/* I/O error for this device */
            public byte rc_tried;		/* Did we attempt this I/O column? */
            public byte rc_skipped;		/* Did we skip this I/O column? */
        }

        [StructLayout(LayoutKind.Sequential)]
        struct raidz_map
        {
            public ulong rm_cols;		/* Regular column count */
            public ulong rm_scols;		/* Count including skipped columns */
            public ulong rm_bigcols;		/* Number of oversized columns */
            public ulong rm_asize;		/* Actual total I/O size */
            public ulong rm_missingdata;	/* Count of missing data devices */
            public ulong rm_missingparity;	/* Count of missing parity devices */
            public ulong rm_firstdatacol;	/* First data column/parity count */
            public ulong rm_nskip;		/* Skipped sectors for padding */
            public ulong rm_skipstart;	/* Column index of padding start */
            //public void* rm_datacopy;		/* rm_asize-buffer of copied data */
            public UIntPtr rm_reports;		/* # of referencing checksum reports */
            public byte rm_freed;		/* map no longer has referencing ZIO */
            public byte rm_ecksuminjected;	/* checksum error was injected */
            public raidz_col[] rm_col;		/* Flexible array of I/O columns */
        }

        static ulong roundup(ulong x, ulong y)
        {
            return ((((x) + ((y) - 1)) / (y)) * (y));
        }

        static raidz_map vdev_raidz_map_alloc(ulong size, ulong offset, int unit_shift, ulong dcols, ulong nparity)
        {
            raidz_map rm;
            ulong b = offset >> unit_shift;
            ulong s = size >> unit_shift;
            ulong f = b % dcols;
            ulong o = (b / dcols) << unit_shift;
            ulong q, r, c, bc, col, acols, scols, coff, devidx, asize, tot;

            q = s / (dcols - nparity);
            r = s - q * (dcols - nparity);
            bc = (r == 0 ? 0 : r + nparity);
            tot = s + nparity * (q + (ulong)(r == 0 ? 0 : 1));

            if (q == 0)
            {
                acols = bc;
                scols = Math.Min(dcols, roundup(bc, nparity + 1));
            }
            else
            {
                acols = dcols;
                scols = dcols;
            }


            Debug.Assert(acols <= scols);

            rm = default(raidz_map);
            rm.rm_col = new raidz_col[scols];

            rm.rm_cols = acols;
            rm.rm_scols = scols;
            rm.rm_bigcols = bc;
            rm.rm_skipstart = bc;
            rm.rm_missingdata = 0;
            rm.rm_missingparity = 0;
            rm.rm_firstdatacol = nparity;
            //rm.rm_datacopy = (void*)0;
            rm.rm_reports = UIntPtr.Zero;
            rm.rm_freed = 0;
            rm.rm_ecksuminjected = 0;

            asize = 0;

            for (c = 0; c < scols; c++)
            {
                col = f + c;
                coff = o;
                if (col >= dcols)
                {
                    col -= dcols;
                    coff += 1UL << unit_shift;
                }
                rm.rm_col[c].rc_devidx = col;
                rm.rm_col[c].rc_offset = coff;
                //rm.rm_col[c].rc_data = NULL;
                //rm.rm_col[c].rc_gdata = NULL;
                rm.rm_col[c].rc_error = 0;
                rm.rm_col[c].rc_tried = 0;
                rm.rm_col[c].rc_skipped = 0;

                if (c >= acols)
                    rm.rm_col[c].rc_size = 0;
                else if (c < bc)
                    rm.rm_col[c].rc_size = (q + 1) << unit_shift;
                else
                    rm.rm_col[c].rc_size = q << unit_shift;

                asize += rm.rm_col[c].rc_size;
            }

            Debug.Assert(asize == (tot << unit_shift));
            rm.rm_asize = roundup(asize, (nparity + 1) << unit_shift);
            rm.rm_nskip = roundup(tot, nparity + 1) - tot;
            Debug.Assert((rm.rm_asize - asize) == (rm.rm_nskip << unit_shift));
            Debug.Assert(rm.rm_nskip <= nparity);

            /*
             * If all data stored spans all columns, there's a danger that parity
             * will always be on the same device and, since parity isn't read
             * during normal operation, that that device's I/O bandwidth won't be
             * used effectively. We therefore switch the parity every 1MB.
             *
             * ... at least that was, ostensibly, the theory. As a practical
             * matter unless we juggle the parity between all devices evenly, we
             * won't see any benefit. Further, occasional writes that aren't a
             * multiple of the LCM of the number of children and the minimum
             * stripe width are sufficient to avoid pessimal behavior.
             * Unfortunately, this decision created an implicit on-disk format
             * requirement that we need to support for all eternity, but only
             * for single-parity RAID-Z.
             *
             * If we intend to skip a sector in the zeroth column for padding
             * we must make sure to note this swap. We will never intend to
             * skip the first column since at least one data and one parity
             * column must appear in each row.
             */
            Debug.Assert(rm.rm_cols >= 2);
            Debug.Assert(rm.rm_col[0].rc_size == rm.rm_col[1].rc_size);

            if (rm.rm_firstdatacol == 1 && (offset & (1UL << 20)) != 0)
            {
                devidx = rm.rm_col[0].rc_devidx;
                o = rm.rm_col[0].rc_offset;
                rm.rm_col[0].rc_devidx = rm.rm_col[1].rc_devidx;
                rm.rm_col[0].rc_offset = rm.rm_col[1].rc_offset;
                rm.rm_col[1].rc_devidx = devidx;
                rm.rm_col[1].rc_offset = o;

                if (rm.rm_skipstart == 0)
                    rm.rm_skipstart = 1;
            }

            return (rm);
        }
    }
}
