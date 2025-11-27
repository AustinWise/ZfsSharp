using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZfsSharpLib
{
    class ObjectSet
    {
        readonly DNode mMetaDNode;
        readonly Zio mZio;
        readonly dmu_objset_type_t mType;

        public ObjectSet(Zio zio, blkptr_t bp)
        {
            if (zio == null)
                throw new ArgumentNullException("zio");
            
            var bytes = zio.ReadBytes(bp);

            if (bytes.Length != objset_phys_t.OBJSET_PHYS_SIZE_V1
                && bytes.Length != objset_phys_t.OBJSET_PHYS_SIZE_V2
                && bytes.Length != objset_phys_t.OBJSET_PHYS_SIZE_V3)
                throw new ArgumentOutOfRangeException();
            
            objset_phys_t os = default;
            bytes.CopyTo(MemoryMarshal.AsBytes(new Span<objset_phys_t>(ref os)));

            mZio = zio;
            mType = os.Type;
            mMetaDNode = new DNode(zio, os.MetaDnode);
        }

        public dmu_objset_type_t Type
        {
            get { return mType; }
        }

        public unsafe DNode ReadEntry(long index)
        {
            var buf = Program.RentBytes(sizeof(dnode_phys_t));
            try
            {
                mMetaDNode.Read(buf, index << dnode_phys_t.DNODE_SHIFT);
                return new DNode(mZio, Program.ToStruct<dnode_phys_t>(buf));
            }
            finally
            {
                Program.ReturnBytes(buf);
            }
        }
    }
}
