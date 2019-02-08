What is this?
=============

This is a C# program that reads ZFS file systems.  Writing is explicitly a non-goal. Several types of disk images are supported: raw, VHD, VHDX, and VDI. RAIDZ, mirror, and standalone vdevs are supported.

Why would you do this?
----------------------

Mostly to increase my understanding on how ZFS works.  Also to see how hard it would be to adapt C# as a systems programming language.

Future plans
------------

* Port to .NET Core to take better advantage of `Span<byte>`.
* Replace dokan support with Windows Projected File System so no third party drivers are required.
* Add support for FUSE.

What I'm learning
-----------------

It was exciting to get enough code working so I could read the uberblock, only to realize that to read any data out of the file system at all I would need to support decompression and checksumming.  This made obvious how fundamentally data integrity is backed into ZFS.

The second moment of zen was to see the bock pointer's abstraction of 128k blocks of copy-on-write data being used by dnodes to create an abstraction of (nearly) arbitrarily big pieces of data.  By merely implementing the dnode and blkptr you get access to the fundamental data management tools of ZFS.  Everything else in ZFS is just reading out of those.

I found it interesting that ZFS incorporates three different ways of storing key-value pairs: ZAP, XDR, and SA.  Admittedly it is a bit of stretch to call SA a key-value store.  Each system has its own performance benefits (SA for when you are storing the same keys in many different place and ZAP for allowing random access.  I have a feeling that XDR was just used for the config data because they already had it lying around).  When I implemented the third system I started to get a feeling of Déjà vu.

What parts of ZFS I wish were better documented
-----------------------------------------------

During the creation of this I found the [ZFS On-Disk Specification][ZfsSpec] to be quite helpful.  However it describes the version one of zpool/zfs.  Illumos forked at zpool version 28 and zfs 5, so there are several aspects of the system that are lacking diagrams in the On-Disk Specification document.  Eventually I'd like to make a blog post full of pretty pictures to describe these structures.

The On-Disk Specification refers to a `znode_phys_t` structure.  In zfs v5 this was replaced with the system attribute (SA) system.  The comment at the start of sa.c is pretty good so it was not difficult to figure out.  I think a nice diagram would have made it even easier to understand.
The description of the Fat ZAP makes sense now that I've implemented it.  However I remember it being somewhat obtuse while implementing it.

The best documentation on how XDR worked was looking at GRUB's code.  Since it only needs to implement enough to boot the system, it quickly gets to the heart of the matter.  So XDR could benefit from more documentation.

[ZfsSpec]: ZFSOnDiskFormat.pdf
