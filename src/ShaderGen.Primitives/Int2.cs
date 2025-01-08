using System;

namespace ShaderGen
{
    public struct Int2
    {
        public int X;
        public int Y;

        public Int2(int value) : this(value, value) { }

        public Int2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int this[int index]
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
