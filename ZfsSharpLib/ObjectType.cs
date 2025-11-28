
namespace ZfsSharpLib;

public readonly struct ObjectType(byte physByte)
{
    private static readonly DmuObjectTypeInfo[] s_dmu_ot = [
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  true,  false, false, "unallocated"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "object directory"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  true,  false, "object array"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  true,  false, false, "packed nvlist"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "packed nvlist size"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "bpobj"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "bpobj header"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "SPA space map header"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "SPA space map"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, true,  "ZIL intent log"),
        new DmuObjectTypeInfo(dmu_object_byteswap.DNODE,  true,  false, true,  "DMU dnode"),
        new DmuObjectTypeInfo(dmu_object_byteswap.OBJSET, true,  true,  false, "DMU objset"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  true,  false, "DSL directory"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL directory child map"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL dataset snap map"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL props"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  true,  false, "DSL dataset"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZNODE,  true,  false, false, "ZFS znode"),
        new DmuObjectTypeInfo(dmu_object_byteswap.OLDACL, true,  false, true,  "ZFS V0 ACL"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  false, false, true,  "ZFS plain file"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,  "ZFS directory"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "ZFS master node"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,  "ZFS delete queue"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  false, false, true,  "zvol object"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "zvol prop"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  false, false, true,  "other uint8[]"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, false, false, true,  "other uint64[]"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "other ZAP"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "persistent error log"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  true,  false, false, "SPA history"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "SPA history offsets"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "Pool properties"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL permissions"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ACL,    true,  false, true,  "ZFS ACL"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  true,  false, true,  "ZFS SYSACL"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  true,  false, true,  "FUID table"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "FUID table size"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL dataset next clones"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "scan work queue"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,  "ZFS user/group/project used"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,  "ZFS user/group/project quota"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "snapshot refcount tags"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "DDT ZAP algorithm"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "DDT statistics"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  true,  false, true,   "System attributes"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,   "SA master node"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,   "SA attr registration"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, true,   "SA attr layouts"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  false, false, "scan translations"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT8,  false, false, true,  "deduplicated block"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL deadlist map"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  true,  false, "DSL deadlist map hdr"),
        new DmuObjectTypeInfo(dmu_object_byteswap.ZAP,    true,  true,  false, "DSL dir clones"),
        new DmuObjectTypeInfo(dmu_object_byteswap.UINT64, true,  false, false, "bpobj subobj"),
    ];

    private readonly bool IsNewType => (physByte & 0x80) != 0;
    internal readonly dmu_object_byteswap Byteswap => IsNewType ? (dmu_object_byteswap)(physByte & 0x1F) : s_dmu_ot[physByte].Byteswap;
    internal readonly dmu_object_type_t LegacyType => IsNewType ? dmu_object_type_t.NONE : (dmu_object_type_t)physByte;
    public readonly bool IsMetadata =>  IsNewType ? (physByte & 0x40) != 0 : s_dmu_ot[physByte].IsMetadata;
    public readonly bool IsEncrypted =>  IsNewType ? (physByte & 0x20) != 0 : s_dmu_ot[physByte].IsEncrypted;

    override public readonly string ToString()
    {
        if (IsNewType)
        {
            return $"(new type) Byteswap={Byteswap}, IsMetadata={IsMetadata}, IsEncrypted={IsEncrypted}";
        }
        else
        {
            return s_dmu_ot[physByte].Name;
        }
    }

    readonly record struct DmuObjectTypeInfo(dmu_object_byteswap Byteswap, bool IsMetadata, bool IsMetadataCached, bool IsEncrypted, string Name);
}
