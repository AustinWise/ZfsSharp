using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZfsSharpLib.HardDisks
{
     partial class FileHardDisk : HardDisk
    {
        private SafeFileHandle mFile;
        private long mSize;

        public FileHardDisk(string path)
        {
            mFile = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            mSize = GetFileLength(mFile);
        }

        public override void ReadBytes(Span<byte> dest, long offset)
        {
            CheckOffsets(offset, dest.Length);
            int read = RandomAccess.Read(mFile, dest, offset);
            if (read != dest.Length)
            {
                throw new IOException("Failed to read the expected number of bytes");
            }
        }

        public override long Length
        {
            get { return mSize; }
        }

        public override void Dispose()
        {
            mFile.Dispose();
        }

        private static long GetFileLength(SafeFileHandle fileHandle)
        {
            if (OperatingSystem.IsWindows())
            {
                // TODO: support block devices on Windows
                return RandomAccess.GetLength(fileHandle);
            }

            int isBlockDevice = IsBlockDevice(fileHandle);
            if (isBlockDevice == -1)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to determine if file is a block device.");
            }
            else if (isBlockDevice == 1)
            {
                long ret = GetBlockDeviceLength(fileHandle);
                if (ret == -1)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to get block device length.");
                }
                return ret;
            }
            else
            {
                return RandomAccess.GetLength(fileHandle);
            }
        }

        [LibraryImport("native_helpers", EntryPoint = "is_block_device", SetLastError = true)]
        private static partial int IsBlockDevice(SafeFileHandle fd);

        [LibraryImport("native_helpers", EntryPoint = "get_block_device_length", SetLastError = true)]
        private static partial long GetBlockDeviceLength(SafeFileHandle fd);
    }
}
