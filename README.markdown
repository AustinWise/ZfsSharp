What is this?
=============

This is a C# program that reads OpenZFS file systems.  Writing is explicitly a non-goal.

Several types of disk images are supported:

* raw
* VHD
* VHDX
* VDI

Several types of VDevs are supported:

* RAIDZ
* mirror
* stripe

A few widely used OpenZFS features are implemented:

* LZ4 compression
* ZSTD compression
* Embedded data in block pointers
* Large blocks

Code Layout
-----------

* ZfsSharpLib: A library for reading ZFS files.
* ZfsProjFs: Mount a pool using [Windows Projected Filesystem][ProjFS].
* Austin.WindowsProjectedFileSystem: A library for using Windows Projected Filesystem. Not specific
  to ZFS.
* ZfsDokan: Mount a pool using [Dokan].
* ZfsSharp: Mostly used for testing and benchmarking.

Future plans
------------

* Some sort of GUI for interactively exploring the on-disk structures of ZFS. Though ideally such a
  GUI would be written on top of `libzpool` for better maintainability and fidelity.
* Add support for FUSE.
* Add `async` support to parallelize checksumming and decompression.

What I'm learning
-----------------

It was exciting to get enough code working so I could read the uberblock in disk label. However it
was a long way from there to reading any information out of the pool as I had to implement
decompression and checksumming.
This made obvious how fundamentally data integrity is backed into ZFS.

The second moment of zen was to see the bock pointer's abstraction of 128k blocks of copy-on-write
data being used by dnodes to create an abstraction of arbitrarily large pieces of data.
By merely implementing the dnode and blkptr abstractions you get access to the fundamental data management tools
of ZFS.  Everything else in ZFS is just reading out of those.

I found it interesting that ZFS incorporates three different ways of storing key-value pairs: ZAP, XDR, and SA.
Each system is designed for different performance profiles:

* SA: compactly storing small amounts of data, where many objects use the same set of keys
* ZAP: fast lookup by key to handle large directories
* XDR (aka NVList): A flexible data format that can support arbitrarily nested data structures (like JSON).
  Its used for rarely changing configs. It was probably used because it was lying around in in the Solaris kernel.
  Perhaps if ZFS was written today, JSON would be used instead.

When I implemented the third system I started to get a feeling of Déjà vu.

What parts of ZFS I wish were better documented
-----------------------------------------------

During the creation of this I found the [ZFS On-Disk Specification][ZfsSpec] to be quite helpful.
However it describes the initial version of zpool and zfs. OpenZFS is forked off OpenSolaris at
zpool version 28 and zfs 5. Since then OpenZFS has added many [features][ZfsFeatures], so there are
several aspects of the system that are lacking diagrams in the On-Disk Specification document.
Eventually I'd like to make a blog post full of pretty pictures to describe these structures.

An example of outdated documentation in the On-Disk Specification is the `znode_phys_t` structure.
In zfs v5 this was replaced with the system attribute (SA) system.  The comment at the start of sa.c
is pretty good so it was not difficult to figure out.  I think a nice diagram would have made it
even easier to understand.

The description of the Fat ZAP makes sense now that I've implemented it. However I remember it
being somewhat obtuse while implementing it.

The best documentation on how XDR worked was looking at GRUB's code. Since it only needs to
implement enough to boot the system, it quickly gets to the heart of the matter.
So XDR could benefit from more documentation.

[ZfsSpec]: ZFSOnDiskFormat.pdf
[ProjFS]: https://docs.microsoft.com/en-us/windows/desktop/projfs/projected-file-system
[Dokan]: https://dokan-dev.github.io/
[ZfsFeatures]: http://www.open-zfs.org/wiki/Feature_Flags
