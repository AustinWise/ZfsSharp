using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Austin.WindowsProjectedFileSystem
{
    public enum CompatibilityStatus
    {
        UnsupportedPlatform,
        UnsupportedArchitecture,
        UnsupportedOsVersion,
        FeatureNotEnabled,
        Supported,
    }

    public static class Compatibility
    {
        public static CompatibilityStatus Status { get; } = getStatus();

        static CompatibilityStatus getStatus()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return CompatibilityStatus.UnsupportedPlatform;
            }

            if (!Environment.Is64BitProcess)
            {
                //It appears that ProjectedFSLib.dll only exists as a 64-bit DLL.
                return CompatibilityStatus.UnsupportedArchitecture;
            }

            //In .NET Core versions before 3, the host executable does not contain a manifest that specifies supportedOs,
            //so Environment.OSVersion.Version lies. So we have to call RtlGetVersion ourselves.
            //In .NET Core 3 a proper manfiest is included:
            //https://github.com/dotnet/core-setup/issues/5106
            var winVer = Interop.NtDll.RtlGetVersion();
            if (winVer.Major < 10)
            {
                //TODO: do we need a tigher version check?
                return CompatibilityStatus.UnsupportedOsVersion;
            }


            //TODO: maybe use DISM API to figure out if the feature is enabled.
            if (!File.Exists(Path.Combine(Environment.SystemDirectory, Interop.ProjectedFfLibDll)))
            {
                return CompatibilityStatus.FeatureNotEnabled;
            }

            return CompatibilityStatus.Supported;
        }
    }
}
