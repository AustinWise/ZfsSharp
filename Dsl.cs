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

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct dsl_dataset_phys_t
    {
        ulong ds_dir_obj;		/* DMU_OT_DSL_DIR */
        ulong ds_prev_snap_obj;	/* DMU_OT_DSL_DATASET */
        ulong ds_prev_snap_txg;
        ulong ds_next_snap_obj;	/* DMU_OT_DSL_DATASET */
        ulong ds_snapnames_zapobj;	/* DMU_OT_DSL_DS_SNAP_MAP 0 for snaps */
        ulong ds_num_children;	/* clone/snap children; ==0 for head */
        ulong ds_creation_time;	/* seconds since 1970 */
        ulong ds_creation_txg;
        ulong ds_deadlist_obj;	/* DMU_OT_DEADLIST */
        /*
         * ds_referenced_bytes, ds_compressed_bytes, and ds_uncompressed_bytes
         * include all blocks referenced by this dataset, including those
         * shared with any other datasets.
         */
        ulong ds_referenced_bytes;
        ulong ds_compressed_bytes;
        ulong ds_uncompressed_bytes;
        ulong ds_unique_bytes;	/* only relevant to snapshots */
        /*
         * The ds_fsid_guid is a 56-bit ID that can change to avoid
         * collisions.  The ds_guid is a 64-bit ID that will never
         * change, so there is a small probability that it will collide.
         */
        ulong ds_fsid_guid;
        ulong ds_guid;
        ulong ds_flags;		/* DS_FLAG_* */
        blkptr_t ds_bp;
        ulong ds_next_clones_obj;	/* DMU_OT_DSL_CLONES */
        ulong ds_props_obj;		/* DMU_OT_DSL_PROPS for snaps */
        ulong ds_userrefs_obj;	/* DMU_OT_USERREFS */
        fixed ulong ds_pad[5]; /* pad out to 320 bytes for good measure */
    }
}
