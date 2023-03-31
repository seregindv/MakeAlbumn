using System;
using System.Drawing;

namespace MakeAlbumn
{
    internal struct ComparableSize : IComparable<ComparableSize>, IComparable
    {
        public int Width { set; get; }
        public int Height { set; get; }

        public ComparableSize(int width, int height)
            : this()
        {
            Width = width;
            Height = height;
        }

        public ComparableSize(Size size)
            : this()
        {
            Width = size.Width;
            Height = size.Height;
        }

        #region Implementation of IComparable<in ComparableSize>

        public int CompareTo(ComparableSize other)
        {
            return ToInt().CompareTo(other.ToInt());
        }

        #endregion

        #region Implementation of IComparable

        public int CompareTo(object obj)
        {
            if (!(obj is ComparableSize))
                return 1;
            return CompareTo((ComparableSize)obj);
        }

        #endregion

        private int ToInt()
        {
            return Width * 100000 + Height;
        }
    }
}
