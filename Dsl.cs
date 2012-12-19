using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace ZfsSharp
{
    class Dsl
    {

    }
    enum dd_used_t
    {
        DD_USED_HEAD,
        DD_USED_SNAP,
        DD_USED_CHILD,
        DD_USED_CHILD_RSRV,
        DD_USED_REFRSRV,
        //DD_USED_NUM
    }
    [StructLayout(LayoutKind.Sequential)]
    unsafe struct dsl_dir_phys_t
    {
        const int DD_USED_NUM = 0x00000005;

        ulong creation_time; /* not actually used */
        public long head_dataset_obj;
        public ulong parent_obj;
        public ulong origin_obj;
        public ulong child_dir_zapobj;
        /*
         * how much space our children are accounting for; for leaf
         * datasets, == physical space used by fs + snaps
         */
        public ulong used_bytes;
        public ulong compressed_bytes;
        public ulong uncompressed_bytes;
        /* Administrative quota setting */
        public ulong quota;
        /* Administrative reservation setting */
        public ulong reserved;
        public ulong props_zapobj;
        public ulong deleg_zapobj; /* dataset delegation permissions */
        public ulong flags;
        public fixed ulong used_breakdown[DD_USED_NUM];
        public ulong clones; /* dsl_dir objects */
        fixed ulong pad[13]; /* pad out to 256 bytes for good measure */
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct dsl_dataset_phys_t
    {
        public ulong dir_obj;		/* DMU_OT_DSL_DIR */
        public ulong prev_snap_obj;	/* DMU_OT_DSL_DATASET */
        public ulong prev_snap_txg;
        public ulong next_snap_obj;	/* DMU_OT_DSL_DATASET */
        public ulong snapnames_zapobj;	/* DMU_OT_DSL_SNAP_MAP 0 for snaps */
        public ulong num_children;	/* clone/snap children; ==0 for head */
        public ulong creation_time;	/* seconds since 1970 */
        public ulong creation_txg;
        public ulong deadlist_obj;	/* DMU_OT_DEADLIST */
        /*
         * referenced_bytes, compressed_bytes, and uncompressed_bytes
         * include all blocks referenced by this dataset, including those
         * shared with any other datasets.
         */
        public ulong referenced_bytes;
        public ulong compressed_bytes;
        public ulong uncompressed_bytes;
        public ulong unique_bytes;	/* only relevant to snapshots */
        /*
         * The fsid_guid is a 56-bit ID that can change to avoid
         * collisions.  The guid is a 64-bit ID that will never
         * change, so there is a small probability that it will collide.
         */
        public ulong fsid_guid;
        public ulong guid;
        public ulong flags;		/* FLAG_* */
        public blkptr_t bp;
        public ulong next_clones_obj;	/* DMU_OT_DSL_CLONES */
        public ulong props_obj;		/* DMU_OT_DSL_PROPS for snaps */
        public ulong userrefs_obj;	/* DMU_OT_USERREFS */
        fixed ulong pad[5]; /* pad out to 320 bytes for good measure */
    }
}
