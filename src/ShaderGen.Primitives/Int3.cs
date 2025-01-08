using System;

namespace ShaderGen
{
    public struct Int3
    {
        public int X;
        public int Y;
        public int Z;

        public Int3(int value) : this(value, value, value) { } 

        public Int3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int this[int index]
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
