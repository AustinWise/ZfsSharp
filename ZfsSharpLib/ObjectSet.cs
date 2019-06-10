using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZfsSharpLib
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
