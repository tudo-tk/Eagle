using System;
using System.Collections.Generic;
using System.Linq;

namespace Eagle.Utils
{
    class ArrayConvert
    {
        internal static T[][] To2DArray<T>(List<T[]> list)
        {
            foreach (T[] t in list)
            {
                if (t == null) throw new ArgumentNullException(t.ToString());
            }

            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].Length != list[i + 1].Length) throw new ArgumentException("input arrays should have the same length");
            }

            var result = new T[list[0].Length][];

            for (int i = 0; i < list[0].Length; i++)
            {
                T[] r = new T[list.Count];

                for (int j = 0; j < list.Count; j++)
                {
                    r[j] = list[j].ElementAt(i);
                }
                result[i] = r;
            }
            return result;
        }

        internal static T[][] ToDoubleArray<T>(List<T[]> list)
        {
            foreach (T[] t in list)
            {
                if (t == null) throw new ArgumentNullException(t.ToString());
            }

            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].Length != list[i + 1].Length) throw new ArgumentException("input arrays should have the same length");
            }

            var result = new T[list.Count][];

            for (int i = 0; i < list.Count; i++)
            {
                T[] r = new T[list[i].Length];

                for (int j = 0; j < list[i].Length; j++)
                {
                    r[j] = list[i].ElementAt(j);
                }
                result[i] = r;
            }
            return result;
        }
    }
}
