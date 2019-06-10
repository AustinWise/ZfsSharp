using System;
using System.Runtime.InteropServices;

namespace ZfsSharpLib
{
    [Flags]
    enum DD_FLAG : ulong
    {
        None = 0,
        USED_BREAKDOWN = 1 << 0
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
        public long parent_obj;
        public long origin_obj;
        public long child_dir_zapobj;
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
        public long props_zapobj;
        public ulong deleg_zapobj; /* dataset delegation permissions */
        public DD_FLAG flags;
        public fixed ulong used_breakdown[DD_USED_NUM];
        public long clones; /* dsl_dir objects */
        fixed ulong pad[13]; /* pad out to 256 bytes for good measure */
    }

    [Flags]
    enum DS_FLAG : ulong
    {
        None = 0,
        INCONSISTENT = (1UL << 0),
        /// <summary>
        /// Do not allow this dataset to be promoted.
        /// </summary>
        NOPROMOTE = (1UL << 1),
        /// <summary>
        /// UNIQUE_ACCURATE is set if ds_unique_bytes has been correctly
        /// calculated for head datasets (starting with SPA_VERSION_UNIQUE_ACCURATE,
        /// refquota/refreservations).
        /// </summary>
        UNIQUE_ACCURATE = (1UL << 2),
        /// <summary>
        /// DEFER_DESTROY is set after 'zfs destroy -d' has been called
        /// on a dataset. This allows the dataset to be destroyed using 'zfs release'.
        /// </summary>
        DEFER_DESTROY = (1UL << 3),
        /// <summary>
        /// CI_DATASET is set if the dataset contains a file system whose
        /// name lookups should be performed case-insensitively.
        /// </summary>
        CI_DATASET = (1UL << 16),
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct dsl_dataset_phys_t
    {
        public long dir_obj;		/* DMU_OT_DSL_DIR */
        public long prev_snap_obj;	/* DMU_OT_DSL_DATASET */
        public ulong prev_snap_txg;
        public long next_snap_obj;	/* DMU_OT_DSL_DATASET */
        public long snapnames_zapobj;	/* DMU_OT_DSL_SNAP_MAP 0 for snaps */
        public ulong num_children;	/* clone/snap children; ==0 for head */
        public ulong creation_time;	/* seconds since 1970 */
        public ulong creation_txg;
        public long deadlist_obj;	/* DMU_OT_DEADLIST */
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
        public DS_FLAG flags;
        public blkptr_t bp;
        public long next_clones_obj;	/* DMU_OT_DSL_CLONES */
        public long props_obj;		/* DMU_OT_DSL_PROPS for snaps */
        public long userrefs_obj;	/* DMU_OT_USERREFS */
        fixed ulong pad[5]; /* pad out to 320 bytes for good measure */
    }
}
