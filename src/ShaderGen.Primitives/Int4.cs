using System;

namespace ShaderGen
{
    public struct Int4
    {
        public int X;
        public int Y;
        public int Z;
        public int W;

        public Int4(int value) : this(value, value, value, value) { }

        public Int4(int x, int y, int z, int w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public int this[int index]
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
