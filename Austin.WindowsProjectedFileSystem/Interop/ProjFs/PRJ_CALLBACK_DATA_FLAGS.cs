﻿using System;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            [Flags]
            public enum PRJ_CALLBACK_DATA_FLAGS : UInt32
            {
                None = 0,
                RESTART_SCAN = 0x00000001,
                RETURN_SINGLE_ENTRY = 0x00000002,
            }
        }
    }
}