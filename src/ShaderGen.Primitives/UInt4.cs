using System;

namespace ShaderGen
{
    public struct UInt4
    {
        public uint X;
        public uint Y;
        public uint Z;
        public uint W;

        public UInt4(uint value) : this(value, value, value, value) { }

        public UInt4(uint x, uint y, uint z, uint w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public uint this[int index]
        {
            get => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                3 => W,
                _ => throw new IndexOutOfRangeException(),
            };
            set
            {
                switch (index)
                {
                    case 0:
                        X = value;
                        break;
                    case 1:
                        Y = value;
                        break;
                    case 2:
                        Z = value;
                        break;
                    case 3:
                        W = value;
                        break;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }
    }
}
