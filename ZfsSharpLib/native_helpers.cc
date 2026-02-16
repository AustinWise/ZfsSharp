#include <sys/stat.h>
#include <stdint.h>
#ifdef __linux__
#include <fcntl.h>
#include <linux/fs.h>
#include <sys/ioctl.h>
#include <unistd.h>
#else
#error "Unsupported platform"
#endif

extern "C" int32_t is_block_device(int fd)
{
    struct stat file_stat{};
    if (fstat(fd, &file_stat) == -1)
    {
        return -1;
    }
    return (file_stat.st_mode &  S_IFMT) == S_IFBLK ? 1 : 0;
}

extern "C" int64_t get_block_device_length(int fd)
{
#ifdef __linux__
    uint64_t size;
    if (ioctl(fd, BLKGETSIZE64, &size) == -1)
    {
        return -1;
    }
    return static_cast<int64_t>(size);
#else
#error "Unsupported platform"
#endif
}
