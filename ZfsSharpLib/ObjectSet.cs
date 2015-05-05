using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class ObjectSet
    {
        readonly DNode mMetaDNode;
        readonly Zio mZio;
        readonly dmu_objset_type_t mType;

        public ObjectSet(Zio zio, objset_phys_t os)
        {
            if (zio == null)
                throw new ArgumentNullException("zio");

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
            var dnStuff = mMetaDNode.Read(index << dnode_phys_t.DNODE_SHIFT, sizeof(dnode_phys_t));
            return new DNode(mZio, Program.ToStruct<dnode_phys_t>(dnStuff));
        }

        public byte[] ReadContent(long index)
        {
            var dn = ReadEntry(index);
            return dn.Read();
        }

        public byte[] ReadContent(long index, long offset, int size)
        {
            var dn = ReadEntry(index);
            return dn.Read(offset, size);
        }

        public void ReadContent(long index, byte[] buffer, long offset, int size)
        {
            var dn = ReadEntry(index);
            dn.Read(buffer, offset, size);
        }
    }
}
