using System;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        partial class ProjFs
        {
            public delegate Int32 PRJ_END_DIRECTORY_ENUMERATION_CB(in PRJ_CALLBACK_DATA callbackData, in Guid enumerationId);
        }
    }
}