using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp
{
    class Zpl
    {
        private Zap mZap;
        private Dmu mDmu;
        private Zio mZio;
        private dsl_dir_phys_t mDslDir;
        private dsl_dataset_phys_t mDataset;
        private objset_phys_t mZfsObjset;
        private Dictionary<string, long> mZfsObjDir;

        private Dictionary<zpl_attr_t, int> mAttrSize = new Dictionary<zpl_attr_t, int>();
        private Dictionary<int, SaLayout> mAttrLayouts = new Dictionary<int, SaLayout>();

        static readonly int SaHdrLengthOffset = Marshal.OffsetOf(typeof(sa_hdr_phys_t), "sa_lengths").ToInt32();

        public Zpl(objset_phys_t mos, long objectid, Zap zap, Dmu dmu, Zio zio)
        {
            this.mZap = zap;
            this.mDmu = dmu;
            this.mZio = zio;

            var rootDslObj = dmu.ReadFromObjectSet(mos, objectid);
            if (rootDslObj.Type != dmu_object_type_t.DSL_DIR)
                throw new NotSupportedException("Expected DSL_DIR dnode.");
            mDslDir = dmu.GetBonus<dsl_dir_phys_t>(rootDslObj);
            var rootDslProps = zap.Parse(dmu.ReadFromObjectSet(mos, mDslDir.props_zapobj));

            var rootDataSetObj = dmu.ReadFromObjectSet(mos, mDslDir.head_dataset_obj);
            if (rootDataSetObj.Type != dmu_object_type_t.DSL_DATASET)
                throw new Exception("Not a DSL_DIR.");
            mDataset = dmu.GetBonus<dsl_dataset_phys_t>(rootDataSetObj);

            mZfsObjset = zio.Get<objset_phys_t>(mDataset.bp);
            if (mZfsObjset.Type != dmu_objset_type_t.DMU_OST_ZFS)
                throw new NotSupportedException();
            mZfsObjDir = zap.GetDirectoryEntries(dmu.ReadFromObjectSet(mZfsObjset, 1));
            if (mZfsObjDir["VERSION"] != 5)
                throw new NotSupportedException();

            var saAttrsDn = dmu.ReadFromObjectSet(mZfsObjset, mZfsObjDir["SA_ATTRS"]);
            var saAttrs = zap.GetDirectoryEntries(saAttrsDn);
            var saLayouts = zap.Parse(dmu.ReadFromObjectSet(mZfsObjset, saAttrs["LAYOUTS"]));
            var saRegistry = zap.GetDirectoryEntries(dmu.ReadFromObjectSet(mZfsObjset, saAttrs["REGISTRY"]));

            mAttrSize = new Dictionary<zpl_attr_t, int>();
            foreach (var kvp in saRegistry)
            {
                var attrName = (zpl_attr_t)(kvp.Value & 0xffff);
                var size = (int)(kvp.Value >> 24 & 0xffff);
                if (kvp.Key != attrName.ToString())
                    throw new Exception();
                mAttrSize.Add(attrName, size);
            }

            foreach (var layoutName in saLayouts.Keys)
            {
                var layoutNumber = int.Parse(layoutName);
                var types = ((short[])saLayouts[layoutName]).Select(a => (zpl_attr_t)a).ToArray();
                mAttrLayouts.Add(layoutNumber, new SaLayout(mAttrSize, types));
            }
        }

        public ZfsDirectory Root
        {
            get
            {
                var dn = mDmu.ReadFromObjectSet(mZfsObjset, mZfsObjDir["ROOT"]);
                return new ZfsDirectory(this, dn);
            }
        }

        public byte[] GetFileContents(string path)
        {
            var fileId = GetFsObjectId(path);
            var fileDn = mDmu.ReadFromObjectSet(mZfsObjset, fileId);
            if (fileDn.Type != dmu_object_type_t.PLAIN_FILE_CONTENTS)
                throw new NotSupportedException();
            var fileSize = GetFileSize(fileDn);
            var fileContents = mDmu.Read(fileDn, 0, fileSize);
            return fileContents;
        }

        private long GetFsObjectId(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/')
                throw new ArgumentOutOfRangeException("Must be absolute path.");

            long id = mZfsObjDir["ROOT"];
            if (path == "/")
                return id;

            var pathParts = path.Substring(1).Split('/');
            foreach (var p in pathParts)
            {
                var dir = mZap.GetDirectoryEntries(mDmu.ReadFromObjectSet(mZfsObjset, id));
                id = dir[p];
            }

            return id;
        }

        public long GetFileSize(dnode_phys_t dn)
        {
            return GetAttr<long>(dn, zpl_attr_t.ZPL_SIZE);
        }

        public unsafe T GetAttr<T>(dnode_phys_t dn, zpl_attr_t attr) where T : struct
        {
            var bytes = mDmu.ReadBonus(dn);
            var saHeader = Program.ToStruct<sa_hdr_phys_t>(bytes);
            saHeader.VerifyMagic();

            var numberOfLengths = 1;
            if (saHeader.hdrsz > 8)
                numberOfLengths += (saHeader.hdrsz - 8) >> 1;

            var layout = mAttrLayouts[saHeader.layout];
            var varSizes = new short[layout.NumberOfVariableSizedFields];

            for (int i = 0; i < varSizes.Length; i++)
            {
                varSizes[i] = Program.ToStruct<short>(bytes, SaHdrLengthOffset + i * 2);
            }

            var fieldOffset = layout.GetOffset(attr, varSizes);
            fieldOffset += saHeader.hdrsz;

            if (mAttrSize[attr] != Marshal.SizeOf(typeof(T)))
                throw new Exception("Unexpected size.");

            return Program.ToStruct<T>(bytes, fieldOffset);
        }

        class SaLayout
        {
            private Dictionary<zpl_attr_t, int> mSizes;
            private zpl_attr_t[] mLayout;
            private int mNumberOfVariableSizedFields;

            public SaLayout(Dictionary<zpl_attr_t, int> sizes, zpl_attr_t[] layout)
            {
                mSizes = sizes;
                mLayout = layout;
                mNumberOfVariableSizedFields = layout.Select(attr => sizes[attr]).Count(size => size == 0);
            }

            public int NumberOfVariableSizedFields
            {
                get { return mNumberOfVariableSizedFields; }
            }

            public int GetOffset(zpl_attr_t attrToGet, short[] variableFieldSizes)
            {
                if (variableFieldSizes.Length != mNumberOfVariableSizedFields)
                    throw new ArgumentOutOfRangeException();
                int fieldOffset = 0;
                int varSizeOffset = 0;
                for (int fieldNdx = 0; fieldNdx < mLayout.Length; fieldNdx++)
                {
                    var attr = mLayout[fieldNdx];
                    if (attr == attrToGet)
                        return fieldOffset;
                    int fieldSize = mSizes[attr];
                    if (fieldSize == 0)
                        fieldSize = variableFieldSizes[varSizeOffset++];
                    fieldOffset += fieldSize;
                }
                throw new ArgumentOutOfRangeException();
            }
        }

        public abstract class ZfsItem
        {
            protected readonly Zpl mZpl;
            protected readonly dnode_phys_t mDn;
            public ZfsItem(Zpl zpl, ZfsDirectory parent, string name, dnode_phys_t dn)
            {
                this.mZpl = zpl;
                this.Name = name;
                this.Parent = parent;
                this.mDn = dn;
                if (mDn.Type != DmuType)
                    throw new NotSupportedException();
            }

            public string Name { get; private set; }
            public ZfsDirectory Parent { get; protected set; }

            protected abstract dmu_object_type_t DmuType { get; }

            public virtual string FullPath
            {
                get
                {
                    var parents = new Stack<ZfsItem>();
                    parents.Push(this);
                    var parent = this.Parent;
                    while (!parent.IsRoot)
                    {
                        parents.Push(parent);
                        parent = parent.Parent;
                    }

                    var path = new StringBuilder();
                    while (parents.Count != 0)
                    {
                        var item = parents.Pop();
                        path.Append('/');
                        path.Append(item.Name);
                    }
                    return path.ToString();
                }
            }

            public override string ToString()
            {
                return Name;
            }
        }

        public class ZfsFile : ZfsItem
        {
            public ZfsFile(Zpl zpl, ZfsDirectory parent, string name, dnode_phys_t dn)
                : base(zpl, parent, name, dn)
            {
            }

            public byte[] GetContents()
            {
                return mZpl.mDmu.Read(mDn, 0, mZpl.GetAttr<long>(mDn, zpl_attr_t.ZPL_SIZE));
            }

            protected override dmu_object_type_t DmuType
            {
                get { return dmu_object_type_t.PLAIN_FILE_CONTENTS; }
            }
        }

        public class ZfsDirectory : ZfsItem
        {
            public ZfsDirectory(Zpl zpl, dnode_phys_t dn)
                : base(zpl, null, "/", dn)
            {
                this.Parent = this;
            }
            public ZfsDirectory(Zpl zpl, ZfsDirectory parent, string name, dnode_phys_t dn)
                : base(zpl, parent, name, dn)
            {
            }

            public bool IsRoot
            {
                get
                {
                    return this.Parent == this;
                }
            }

            protected override dmu_object_type_t DmuType
            {
                get { return dmu_object_type_t.DIRECTORY_CONTENTS; }
            }

            public IEnumerable<ZfsItem> GetChildren()
            {
                var dirContents = mZpl.mZap.GetDirectoryEntries(mDn);
                foreach (var kvp in dirContents)
                {
                    string name = kvp.Key;
                    long objId = kvp.Value;
                    var dn = mZpl.mDmu.ReadFromObjectSet(mZpl.mZfsObjset, objId);

                    if (dn.Type == dmu_object_type_t.DIRECTORY_CONTENTS)
                        yield return new ZfsDirectory(mZpl, this, name, dn);
                    else if (dn.Type == dmu_object_type_t.PLAIN_FILE_CONTENTS)
                        yield return new ZfsFile(mZpl, this, name, dn);
                    else
                        throw new NotSupportedException();
                }
            }

            public override string FullPath
            {
                get
                {
                    if (IsRoot)
                        return "/";
                    return base.FullPath;
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct sa_hdr_phys_t
    {
        const uint SA_MAGIC = 0x2F505A;

        uint sa_magic;
        ushort sa_layout_info;  /* Encoded with hdrsize and layout number */
        public fixed ushort sa_lengths[1];	/* optional sizes for variable length attrs */
        /* ... Data follows the lengths.  */

        public int hdrsz
        {
            get { return (sa_layout_info >> 10) * 8; }
        }
        public int layout
        {
            get { return sa_layout_info & 0x3FF; }
        }

        public void VerifyMagic()
        {
            if (sa_magic != SA_MAGIC)
                throw new Exception();
        }
    }
    enum zpl_attr_t : short
    {
        ZPL_ATIME = 0,
        ZPL_MTIME,
        ZPL_CTIME,
        ZPL_CRTIME,
        ZPL_GEN,
        ZPL_MODE,
        ZPL_SIZE,
        ZPL_PARENT,
        ZPL_LINKS,
        ZPL_XATTR,
        ZPL_RDEV,
        ZPL_FLAGS,
        ZPL_UID,
        ZPL_GID,
        ZPL_PAD,
        ZPL_ZNODE_ACL,
        ZPL_DACL_COUNT,
        ZPL_SYMLINK,
        ZPL_SCANSTAMP,
        ZPL_DACL_ACES,
        //ZPL_END
    }
}
