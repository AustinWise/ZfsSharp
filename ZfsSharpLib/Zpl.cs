﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZfsSharp
{
    public class Zpl
    {
        private Zio mZio;
        private dsl_dataset_phys_t mDataset;
        private ObjectSet mZfsObjset;
        private Dictionary<string, long> mZfsObjDir;

        private Dictionary<zpl_attr_t, int> mAttrSize = new Dictionary<zpl_attr_t, int>();
        private Dictionary<int, SaLayout> mAttrLayouts = new Dictionary<int, SaLayout>();

        static readonly int SaHdrLengthOffset = Marshal.OffsetOf(typeof(sa_hdr_phys_t), "sa_lengths").ToInt32();

        internal Zpl(ObjectSet mos, long objectid, Zio zio)
        {
            this.mZio = zio;

            var rootDataSetObj = mos.ReadEntry(objectid);
            if (!DatasetDirectory.IsDataSet(rootDataSetObj))
                throw new Exception("Not a DSL_DIR.");
            mDataset = rootDataSetObj.GetBonus<dsl_dataset_phys_t>();

            if (rootDataSetObj.IsNewType && rootDataSetObj.NewType == dmu_object_byteswap.DMU_BSWAP_ZAP)
            {
                var dataSetExtensions = Zap.Parse(rootDataSetObj);
            }

            if (mDataset.prev_snap_obj != 0)
            {
                var dn = mos.ReadEntry(mDataset.prev_snap_obj);
                var moreDs = dn.GetBonus<dsl_dataset_phys_t>();
            }

            if (mDataset.props_obj != 0)
            {
                var someProps = Zap.Parse(mos, mDataset.props_obj);
            }

            mZfsObjset = new ObjectSet(mZio, zio.Get<objset_phys_t>(mDataset.bp));
            if (mZfsObjset.Type != dmu_objset_type_t.DMU_OST_ZFS)
                throw new NotSupportedException();
            mZfsObjDir = Zap.GetDirectoryEntries(mZfsObjset, 1);
            if (mZfsObjDir["VERSION"] != 5)
                throw new NotSupportedException();

            var saAttrs = Zap.GetDirectoryEntries(mZfsObjset, mZfsObjDir["SA_ATTRS"]);
            var saLayouts = Zap.Parse(mZfsObjset.ReadEntry(saAttrs["LAYOUTS"]));
            var saRegistry = Zap.GetDirectoryEntries(mZfsObjset, saAttrs["REGISTRY"]);

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
                var dn = mZfsObjset.ReadEntry(mZfsObjDir["ROOT"]);
                return new ZfsDirectory(this, dn);
            }
        }

        public byte[] GetFileContents(string path)
        {
            var fileId = GetFsObjectId(path);
            var fileDn = mZfsObjset.ReadEntry(fileId);
            var file = new Zpl.ZfsFile(this, null, path, fileDn);
            return file.GetContents();
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
                var dir = Zap.GetDirectoryEntries(mZfsObjset, id);
                id = dir[p];
            }

            return id;
        }

        long GetFileSize(DNode dn)
        {
            return GetAttr<long>(dn, zpl_attr_t.ZPL_SIZE);
        }

        ZfsItemType GetFileType(DNode dn)
        {
            var mode = GetAttr<long>(dn, zpl_attr_t.ZPL_MODE);
            return (ZfsItemType)((mode >> 12) & 0xf);
        }

        T GetAttr<T>(DNode dn, zpl_attr_t attr) where T : struct
        {
            var bytes = GetAttrBytes(dn, attr);

            return Program.ToStruct<T>(bytes);
        }

        ArraySegment<byte> GetAttrBytes(DNode dn, zpl_attr_t attr)
        {
            var bytes = dn.ReadBonus();
            ArraySegment<byte> ret;
            if (GetAttrFromBytes(bytes, attr, out ret))
            {
                return ret;
            }

            if (dn.SpillType == dmu_object_type_t.SA)
            {
                bytes = dn.ReadSpill();
                if (GetAttrFromBytes(bytes, attr, out ret))
                {
                    return ret;
                }
            }

            throw new KeyNotFoundException();
        }

        private bool GetAttrFromBytes(byte[] bytes, zpl_attr_t attr, out ArraySegment<byte> ret)
        {
            var saHeader = Program.ToStruct<sa_hdr_phys_t>(bytes);
            saHeader.VerifyMagic();

            var numberOfLengths = 1;
            if (saHeader.hdrsz > 8)
                numberOfLengths += (saHeader.hdrsz - 8) >> 1;

            var layout = mAttrLayouts[saHeader.layout];

            if (!layout.ContainsAttr(attr))
            {
                ret = default(ArraySegment<byte>);
                return false;
            }

            var varSizes = new short[layout.NumberOfVariableSizedFields];
            for (int i = 0; i < varSizes.Length; i++)
            {
                varSizes[i] = Program.ToStruct<short>(bytes, SaHdrLengthOffset + i * 2);
            }

            var fieldOffset = layout.GetOffset(attr, varSizes);
            fieldOffset += saHeader.hdrsz;

            var fieldSize = layout.GetFieldSize(attr, varSizes);
            ret = new ArraySegment<byte>(bytes, fieldOffset, fieldSize);
            return true;
        }

        class SaLayout
        {
            private Dictionary<zpl_attr_t, int> mVarSizeOffset = new Dictionary<zpl_attr_t, int>();
            private Dictionary<zpl_attr_t, int> mSizes;
            private zpl_attr_t[] mLayout;
            private int mNumberOfVariableSizedFields;

            public SaLayout(Dictionary<zpl_attr_t, int> sizes, zpl_attr_t[] layout)
            {
                mSizes = sizes;
                mLayout = layout;
                mNumberOfVariableSizedFields = layout.Select(attr => sizes[attr]).Count(size => size == 0);

                var varSizeFields = mLayout.Where(a => sizes[a] == 0).ToArray();
                for (int i = 0; i < varSizeFields.Length; i++)
                {
                    mVarSizeOffset[varSizeFields[i]] = i;
                }
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

            public int GetFieldSize(zpl_attr_t attrToGet, short[] variableFieldSizes)
            {
                if (variableFieldSizes.Length != mNumberOfVariableSizedFields)
                    throw new ArgumentOutOfRangeException();
                var size = mSizes[attrToGet];
                if (size == 0)
                {
                    size = variableFieldSizes[mVarSizeOffset[attrToGet]];
                }
                return size;
            }

            public bool ContainsAttr(zpl_attr_t attr)
            {
                return mLayout.Any(a => a == attr);
            }
        }

        public abstract class ZfsItem
        {
            static readonly DateTime sEpoc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            protected readonly Zpl mZpl;
            internal readonly DNode mDn;
            protected readonly long mMode;
            internal ZfsItem(Zpl zpl, ZfsDirectory parent, string name, DNode dn)
            {
                this.mZpl = zpl;
                this.Name = name;
                this.Parent = parent;
                this.mDn = dn;
                if (mDn.Type != DmuType)
                    throw new NotSupportedException();

                mMode = mZpl.GetAttr<long>(mDn, zpl_attr_t.ZPL_MODE);

                CTIME = GetDateTime(zpl_attr_t.ZPL_CTIME);
                MTIME = GetDateTime(zpl_attr_t.ZPL_MTIME);
                ATIME = GetDateTime(zpl_attr_t.ZPL_ATIME);
            }

            DateTime GetDateTime(zpl_attr_t attr)
            {
                var bytes = mZpl.GetAttrBytes(mDn, attr);
                ulong seconds = Program.ToStruct<ulong>(new ArraySegment<byte>(bytes.Array, bytes.Offset, 8));
                ulong nanosecs = Program.ToStruct<ulong>(new ArraySegment<byte>(bytes.Array, bytes.Offset + 8, 8));
                return sEpoc.AddSeconds(seconds).AddTicks((long)(nanosecs / 100));
            }

            public string Name { get; private set; }
            public DateTime CTIME { get; private set; }
            public DateTime MTIME { get; private set; }
            public DateTime ATIME { get; private set; }
            public ZfsDirectory Parent { get; protected set; }

            internal abstract dmu_object_type_t DmuType { get; }

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

            public ZfsItemType Type
            {
                get
                {
                    return (ZfsItemType)((mMode >> 12) & 0xf);
                }
            }

            public override string ToString()
            {
                return Name;
            }
        }

        public class ZfsFile : ZfsItem
        {
            internal ZfsFile(Zpl zpl, ZfsDirectory parent, string name, DNode dn)
                : base(zpl, parent, name, dn)
            {
                Length = mZpl.GetAttr<long>(mDn, zpl_attr_t.ZPL_SIZE);
            }

            public byte[] GetContents()
            {
                if (Length > int.MaxValue || mDn.AvailableDataSize > int.MaxValue)
                    throw new Exception("Too much data to read all at once.");
                var ret = new byte[Length];
                mDn.Read(ret, 0, (int)Math.Min(Length, mDn.AvailableDataSize));
                return ret;
            }

            public byte[] GetContents(long offset, int count)
            {
                //TODO: deal with files whose length is greater than the data allocated by the DNode
                return mDn.Read(offset, count);
            }

            public void GetContents(byte[] buffer, long offset, int count)
            {
                //TODO: deal with files whose length is greater than the data allocated by the DNode
                mDn.Read(buffer, offset, count);
            }

            public long Length { get; private set; }

            internal override dmu_object_type_t DmuType
            {
                get { return dmu_object_type_t.PLAIN_FILE_CONTENTS; }
            }
        }

        public class ZfsSymLink : ZfsItem
        {
            internal ZfsSymLink(Zpl zpl, ZfsDirectory parent, string name, DNode dn)
                : base(zpl, parent, name, dn)
            {
                var bytes = zpl.GetAttrBytes(dn, zpl_attr_t.ZPL_SYMLINK);
                PointsTo = Encoding.ASCII.GetString(bytes.Array, bytes.Offset, bytes.Count);
            }

            public string PointsTo { get; private set; }

            internal override dmu_object_type_t DmuType
            {
                get { return dmu_object_type_t.PLAIN_FILE_CONTENTS; }
            }
        }

        public class ZfsDirectory : ZfsItem
        {
            internal ZfsDirectory(Zpl zpl, DNode dn)
                : base(zpl, null, "/", dn)
            {
                this.Parent = this;
            }
            internal ZfsDirectory(Zpl zpl, ZfsDirectory parent, string name, DNode dn)
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

            internal override dmu_object_type_t DmuType
            {
                get { return dmu_object_type_t.DIRECTORY_CONTENTS; }
            }

            public IEnumerable<ZfsItem> GetChildren()
            {
                var dirContents = Zap.GetDirectoryEntries(mDn);
                foreach (var kvp in dirContents)
                {
                    string name = kvp.Key;
                    long objId = kvp.Value;
                    var dn = mZpl.mZfsObjset.ReadEntry(objId);

                    if (dn.Type == dmu_object_type_t.DIRECTORY_CONTENTS)
                        yield return new ZfsDirectory(mZpl, this, name, dn);
                    else if (dn.Type == dmu_object_type_t.PLAIN_FILE_CONTENTS)
                    {
                        var type = mZpl.GetFileType(dn);
                        if (type == ZfsItemType.S_IFREG)
                        {
                            yield return new ZfsFile(mZpl, this, name, dn);
                        }
                        else if (type == ZfsItemType.S_IFLNK)
                        {
                            yield return new ZfsSymLink(mZpl, this, name, dn);
                        }
                        else
                        {
                            //TODO: other file types
                        }
                    }
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
}
