using System;

namespace ShaderGen
{
    public struct UInt2
    {
        public uint X;
        public uint Y;

        public UInt2(uint value) : this(value, value) { }

        public UInt2(uint x, uint y)
        {
            X = x;
            Y = y;
        }

        public uint this[int index]
        {
            get => index switch
            {
                0 => X,
                1 => Y,
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
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }
    }
}
