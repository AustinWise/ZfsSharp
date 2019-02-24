using System;
using System.Collections.Generic;
using System.Text;

namespace Austin.WindowsProjectedFileSystem
{
    public interface IProjectedFileSystemCallbacks
    {
        FileBasicInfo[] EnumerateDirectory(bool isWildCardExpression, string searchExpression);
        bool FileExists(string fileName);
    }
}
