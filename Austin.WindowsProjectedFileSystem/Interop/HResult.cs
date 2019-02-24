namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        public static class HResult
        {
            const int FACILITY_NT_BIT = 0x10000000;
            static int HRESULT_FROM_NT(uint status)
            {
                return (int)(status | FACILITY_NT_BIT);
            }

            const int FACILITY_WIN32 = 7;
            static int HRESULT_FROM_WIN32(uint x)
            {
                return (int)(x) <= 0 ? (int)(x) : (int)(((x) & 0x0000FFFF) | (FACILITY_WIN32 << 16) | 0x80000000);
            }

            public static readonly int STATUS_CANNOT_DELETE = HRESULT_FROM_NT(0xC0000121);
            public static readonly int ERROR_FILE_NOT_FOUND = HRESULT_FROM_WIN32(2);
            public static readonly int ERROR_INVALID_PARAMETER = HRESULT_FROM_WIN32(87);
            public static readonly int ERROR_INSUFFICIENT_BUFFER = HRESULT_FROM_WIN32(122);
        }
    }
}