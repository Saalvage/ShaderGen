using System;

namespace ShaderGen
{
    public struct UInt3
    {
        public uint X;
        public uint Y;
        public uint Z;

        public UInt3(uint value) : this(value, value, value) { }

        public UInt3(uint x, uint y, uint z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public uint this[int index]
        {
            get => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
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
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }
    }
}
