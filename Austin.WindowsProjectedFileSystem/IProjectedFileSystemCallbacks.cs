namespace Austin.WindowsProjectedFileSystem
{
    public interface IProjectedFileSystemCallbacks
    {
        FileBasicInfo[] EnumerateDirectory(bool isWildCardExpression, string directory, string searchExpression);
        bool FileExists(string fileName);
        //TODO: add support for extended attributes, etc.
        FileBasicInfo QueryFileInfo(string fileName);
        bool GetFileData(string fileName, byte[] buf, ulong offset, uint length);
    }
}
