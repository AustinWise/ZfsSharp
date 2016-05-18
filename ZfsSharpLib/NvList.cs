using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZfsSharp
{
    //based on usr\src\uts\common\rpc\xdr.c

    /// <summary>
    /// Decodes an XDR representation of a NVList.
    /// </summary>
    class NvList : IEnumerable<KeyValuePair<string, object>>
    {
        const int NV_VERSION = 0;

        enum NvDataType
        {
            UNKNOWN = 0,
            BOOLEAN,
            BYTE,
            INT16,
            UINT16,
            INT32,
            UINT32,
            INT64,
            UINT64,
            STRING,
            BYTE_ARRAY,
            INT16_ARRAY,
            UINT16_ARRAY,
            INT32_ARRAY,
            UINT32_ARRAY,
            INT64_ARRAY,
            UINT64_ARRAY,
            STRING_ARRAY,
            HRTIME,
            NVLIST,
            NVLIST_ARRAY,
            BOOLEAN_VALUE,
            INT8,
            UINT8,
            BOOLEAN_ARRAY,
            INT8_ARRAY,
            UINT8_ARRAY,
        }

        [Flags]
        enum NvFlags : int
        {
            None = 0,
            UNIQUE_NAME = 0x1,
            UNIQUE_NAME_TYPE = 0x2,
        }

        enum NV_ENCODE : byte
        {
            NATIVE = 0,
            XDR = 1,
        }

        private Dictionary<string, object> mVals = new Dictionary<string, object>();

        public NvList(ArraySegment<byte> bytes)
            : this(new MemoryStream(bytes.Array, bytes.Offset, bytes.Count))
        {
        }

        public NvList(byte[] bytes)
            : this(new MemoryStream(bytes))
        {
        }

        public NvList(Stream s)
        {
            var r = new NvListBinaryReader(s, Encoding.ASCII);

            NV_ENCODE encoding = (NV_ENCODE)r.ReadByte();
            if (encoding != NV_ENCODE.XDR)
                throw new Exception("Is not encoding in XDR.");
            byte endian = r.ReadByte();
            if (endian != 1)
                throw new Exception("Incorrect endianness.");
            short reserved = r.ReadInt16(); //reserved fields

            Load(r);
        }

        private NvList(NvListBinaryReader r)
        {
            Load(r);
        }


        private void Load(NvListBinaryReader r)
        {
            int version = r.ReadInt32();

            if (version != NV_VERSION)
                throw new NotSupportedException("Unsupport NVList version!");

            NvFlags flags = (NvFlags)r.ReadInt32();

            while (true)
            {
                int encodedSize = r.ReadInt32();
                int decodedSize = r.ReadInt32();

                if (encodedSize == 0 && decodedSize == 0)
                    break;

                string name = r.ReadString();
                NvDataType type = (NvDataType)r.ReadInt32();
                int numberOfElements = r.ReadInt32();

                object val;
                switch (type)
                {
                    case NvDataType.STRING:
                        val = r.ReadString();
                        break;
                    case NvDataType.UINT64:
                        val = r.ReadUInt64();
                        break;
                    case NvDataType.NVLIST:
                        val = new NvList(r);
                        break;
                    case NvDataType.NVLIST_ARRAY:
                        var array = new NvList[numberOfElements];
                        for (int i = 0; i < numberOfElements; i++)
                        {
                            array[i] = new NvList(r);
                        }
                        val = array;
                        break;
                    case NvDataType.BOOLEAN:
                        val = true;
                        break;
                    case NvDataType.BOOLEAN_VALUE:
                        val = r.ReadInt32() != 0;
                        break;
                    default:
                        throw new NotImplementedException();
                }
                mVals.Add(name, val);
            }
        }

        public T Get<T>(string name)
        {
            return (T)mVals[name];
        }

        public T? GetOptional<T>(string name) where T : struct
        {
            if (!mVals.ContainsKey(name))
                return new Nullable<T>();
            return (T)mVals[name];
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return mVals.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return mVals.GetEnumerator();
        }

    }
}
