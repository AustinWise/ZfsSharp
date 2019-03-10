using System;
using System.Buffers;
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
                using (var attr = RentAttrBytes(dn))
                {
                    return new ZfsDirectory(this, dn, attr);
                }
            }
        }

        public byte[] GetFileContents(string path)
        {
            ZfsFile file = GetFile(path);
            return file.GetContents();
        }

        public ZfsFile GetFile(string path)
        {
            var fileId = GetFsObjectId(path);
            var fileDn = mZfsObjset.ReadEntry(fileId);
            using (var attr = RentAttrBytes(fileDn))
            {
                return new Zpl.ZfsFile(this, null, path, fileDn, attr);
            }
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

        ZfsItemType GetFileType(SaAttributes attr)
        {
            var mode = attr.Get<long>(zpl_attr_t.ZPL_MODE);
            return (ZfsItemType)((mode >> 12) & 0xf);
        }

        internal class SaAttributes : IDisposable
        {
            ArraySegment<byte> mBuf1, mBuf2;
            Dictionary<zpl_attr_t, ArraySegment<byte>> mEntries;

            public SaAttributes(ArraySegment<byte> buf1, ArraySegment<byte> buf2, Dictionary<zpl_attr_t, ArraySegment<byte>> entries)
            {
                this.mBuf1 = buf1;
                this.mBuf2 = buf2;
                this.mEntries = entries;
            }

            public ArraySegment<byte> Get(zpl_attr_t attr)
            {
                return mEntries[attr];
            }

            public T Get<T>(zpl_attr_t attr) where T : struct
            {
                return Program.ToStruct<T>(mEntries[attr]);
            }

            public void Dispose()
            {
                if (mBuf1.Array != null)
                    Program.ReturnBytes(mBuf1);
                if (mBuf2.Array != null)
                    Program.ReturnBytes(mBuf2);
                mEntries = null;
                mBuf1 = default(ArraySegment<byte>);
                mBuf2 = default(ArraySegment<byte>);
            }
        }

        SaAttributes RentAttrBytes(DNode dn)
        {
            var bonusBuf = default(ArraySegment<byte>);
            var spillBuff = default(ArraySegment<byte>);
            var dicret = new Dictionary<zpl_attr_t, ArraySegment<byte>>();

            {
                bonusBuf = dn.RentBonus();
                GetAttrFromBytes(bonusBuf, dicret);
            }

            if (dn.SpillType == dmu_object_type_t.SA)
            {
                spillBuff = Program.RentBytes(dn.SpillSize);
                dn.ReadSpill(spillBuff);
                GetAttrFromBytes(spillBuff, dicret);
            }

            return new SaAttributes(bonusBuf, spillBuff, dicret);
        }

        private void GetAttrFromBytes(ArraySegment<byte> bytes, Dictionary<zpl_attr_t, ArraySegment<byte>> dicret)
        {
            var saHeader = Program.ToStruct<sa_hdr_phys_t>(bytes.Array, bytes.Offset);
            saHeader.VerifyMagic();

            var numberOfLengths = 1;
            if (saHeader.hdrsz > 8)
                numberOfLengths += (saHeader.hdrsz - 8) >> 1;

            var layout = mAttrLayouts[saHeader.layout];

            var pool = ArrayPool<short>.Shared;
            var varSizes = pool.Rent(layout.NumberOfVariableSizedFields);
            for (int i = 0; i < varSizes.Length; i++)
            {
                varSizes[i] = Program.ToStruct<short>(bytes.Array, bytes.Offset + SaHdrLengthOffset + i * 2);
            }

            foreach (var ent in layout.GetEntries(varSizes))
            {
                dicret.Add(ent.Attr, bytes.SubSegment(ent.Offset + saHeader.hdrsz, ent.Size));
            }

            pool.Return(varSizes);
        }

        struct SaLayoutEntry
        {
            public SaLayoutEntry(zpl_attr_t attr, int offset, int size)
            {
                this.Attr = attr;
                this.Offset = offset;
                this.Size = size;
            }
            public zpl_attr_t Attr;
            public int Offset;
            public int Size;
        }

        class SaLayout
        {
            readonly zpl_attr_t[] mLayout;
            readonly int[] mSizes;
            readonly int mNumberOfVariableSizedFields;

            public SaLayout(Dictionary<zpl_attr_t, int> sizes, zpl_attr_t[] layout)
            {
                mLayout = layout;
                mSizes = new int[layout.Length];
                mNumberOfVariableSizedFields = 0;
                for (int i = 0; i < layout.Length; i++)
                {
                    int size = sizes[layout[i]];
                    if (size == 0)
                        mNumberOfVariableSizedFields++;
                    else
                        mSizes[i] = size;
                }
            }

            public int NumberOfVariableSizedFields
            {
                get { return mNumberOfVariableSizedFields; }
            }

            public IEnumerable<SaLayoutEntry> GetEntries(short[] variableFieldSizes)
            {
                if (variableFieldSizes.Length < mNumberOfVariableSizedFields)
                    throw new ArgumentOutOfRangeException(nameof(variableFieldSizes));
                int fieldOffset = 0;
                int varSizeOffset = 0;
                for (int fieldNdx = 0; fieldNdx < mLayout.Length; fieldNdx++)
                {
                    var attr = mLayout[fieldNdx];
                    int fieldSize = mSizes[fieldNdx];
                    if (fieldSize == 0)
                        fieldSize = variableFieldSizes[varSizeOffset++];

                    yield return new SaLayoutEntry(attr, fieldOffset, fieldSize);

                    fieldOffset += fieldSize;
                }
            }
        }

        public abstract class ZfsItem
        {
            static readonly DateTime sEpoc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            protected readonly Zpl mZpl;
            internal readonly DNode mDn;
            protected readonly long mMode;
            internal ZfsItem(Zpl zpl, ZfsDirectory parent, string name, DNode dn, SaAttributes attrs)
            {
                this.mZpl = zpl;
                this.Name = name;
                this.Parent = parent;
                this.mDn = dn;
                if (mDn.Type != DmuType)
                    throw new NotSupportedException();

                mMode = attrs.Get<long>(zpl_attr_t.ZPL_MODE);

                CTIME = GetDateTime(attrs, zpl_attr_t.ZPL_CTIME);
                MTIME = GetDateTime(attrs, zpl_attr_t.ZPL_MTIME);
                ATIME = GetDateTime(attrs, zpl_attr_t.ZPL_ATIME);
            }

            DateTime GetDateTime(SaAttributes attrs, zpl_attr_t attr)
            {
                var bytes = attrs.Get(attr);
                ulong seconds = Program.ToStruct<ulong>(bytes.SubSegment(0, sizeof(ulong)));
                ulong nanosecs = Program.ToStruct<ulong>(bytes.SubSegment(sizeof(ulong), sizeof(ulong)));
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
            internal ZfsFile(Zpl zpl, ZfsDirectory parent, string name, DNode dn, SaAttributes attr)
                : base(zpl, parent, name, dn, attr)
            {
                Length = attr.Get<long>(zpl_attr_t.ZPL_SIZE);
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
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count), "Count must be possitive.");
                //leave other argument validation to the next method

                byte[] ret = new byte[count];
                GetContents(ret, offset);
                return ret;
            }

            public void GetContents(Span<byte> buffer, long offset)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));
                if (offset < 0)
                    throw new ArgumentOutOfRangeException(nameof(offset), "Offset must note be negitive.");
                if (offset + buffer.Length > Length)
                    throw new ArgumentOutOfRangeException(nameof(offset), "Count exceeds available data.");

                int bytesToRead = buffer.Length;
                if (offset + buffer.Length > mDn.AvailableDataSize)
                    bytesToRead = (int)Math.Max(0, mDn.AvailableDataSize - offset);

                if (bytesToRead != 0)
                    mDn.Read(buffer.Slice(0, bytesToRead), offset);
                if (bytesToRead != buffer.Length)
                    buffer.Slice(bytesToRead).Fill(0);
            }

            public long Length { get; }

            internal override dmu_object_type_t DmuType
            {
                get { return dmu_object_type_t.PLAIN_FILE_CONTENTS; }
            }
        }

        public class ZfsSymLink : ZfsItem
        {
            internal ZfsSymLink(Zpl zpl, ZfsDirectory parent, string name, DNode dn, SaAttributes attr)
                : base(zpl, parent, name, dn, attr)
            {
                var bytes = attr.Get(zpl_attr_t.ZPL_SYMLINK);
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
            internal ZfsDirectory(Zpl zpl, DNode dn, SaAttributes attr)
                : base(zpl, null, "/", dn, attr)
            {
                this.Parent = this;
            }
            internal ZfsDirectory(Zpl zpl, ZfsDirectory parent, string name, DNode dn, SaAttributes attr)
                : base(zpl, parent, name, dn, attr)
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
                    ZfsItemType type = (ZfsItemType)((ulong)kvp.Value >> 60);
                    long objId = kvp.Value & (long)~0xF000000000000000;
                    var dn = mZpl.mZfsObjset.ReadEntry(objId);

                    using (var saAttrs = this.mZpl.RentAttrBytes(dn))
                    {
                        if (dn.Type == dmu_object_type_t.DIRECTORY_CONTENTS)
                            yield return new ZfsDirectory(mZpl, this, name, dn, saAttrs);
                        else if (dn.Type == dmu_object_type_t.PLAIN_FILE_CONTENTS)
                        {
                            if (type == ZfsItemType.None)
                                type = mZpl.GetFileType(saAttrs);
                            if (type == ZfsItemType.S_IFREG)
                            {
                                yield return new ZfsFile(mZpl, this, name, dn, saAttrs);
                            }
                            else if (type == ZfsItemType.S_IFLNK)
                            {
                                yield return new ZfsSymLink(mZpl, this, name, dn, saAttrs);
                            }
                            else
                            {
                                //TODO: other file types
                            }
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }

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
