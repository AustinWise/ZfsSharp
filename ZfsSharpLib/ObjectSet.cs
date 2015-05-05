using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class ObjectSet
    {
        readonly dnode_phys_t mMetaDNode;
        readonly Dmu mDmu;
        readonly dmu_objset_type_t mType;

        public ObjectSet(Dmu dmu, objset_phys_t os)
        {
            if (dmu == null)
                throw new ArgumentNullException("dmu");

            mDmu = dmu;
            mType = os.Type;
            mMetaDNode = os.MetaDnode;
        }

        public dmu_objset_type_t Type
        {
            get { return mType; }
        }

        public unsafe dnode_phys_t ReadEntry(long index)
        {
            var dnStuff = mDmu.Read(mMetaDNode, index << dnode_phys_t.DNODE_SHIFT, sizeof(dnode_phys_t));
            return Program.ToStruct<dnode_phys_t>(dnStuff);
        }

        public byte[] ReadContent(long index)
        {
            var dn = ReadEntry(index);
            return mDmu.Read(dn);
        }

        public byte[] ReadContent(long index, long offset, int size)
        {
            var dn = ReadEntry(index);
            return mDmu.Read(dn, offset, size);
        }

        public void ReadContent(long index, byte[] buffer, long offset, int size)
        {
            var dn = ReadEntry(index);
            mDmu.Read(dn, buffer, offset, size);
        }
    }
}
