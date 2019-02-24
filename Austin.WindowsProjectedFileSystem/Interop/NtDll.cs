using System;
using System.Runtime.InteropServices;

namespace Austin.WindowsProjectedFileSystem
{
    partial class Interop
    {
        public static class NtDll
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct RTL_OSVERSIONINFOEX
            {
                internal uint dwOSVersionInfoSize;
                internal uint dwMajorVersion;
                internal uint dwMinorVersion;
                internal uint dwBuildNumber;
                internal uint dwPlatformId;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                internal string szCSDVersion;
            }

            [DllImport("ntdll")]
            private static extern int RtlGetVersion(out RTL_OSVERSIONINFOEX lpVersionInformation);

            internal static Version RtlGetVersion()
            {
                RTL_OSVERSIONINFOEX v = new RTL_OSVERSIONINFOEX();
                v.dwOSVersionInfoSize = (uint)Marshal.SizeOf(v);
                if (RtlGetVersion(out v) == 0)
                {
                    return new Version((int)v.dwMajorVersion, (int)v.dwMinorVersion, (int)v.dwBuildNumber, (int)v.dwPlatformId);
                }
                else
                {
                    throw new Exception("RtlGetVersion failed!");
                }
            }
        }
    }
}
