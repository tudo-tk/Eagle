using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Eagle.Analysis.Cluster
{
    public class DataSetItemPoint : DatasetItemBase
    {
        public double X;
        public double Y;
        public double Z;
        public double W;

        public DataSetItemPoint()
        {
        }

        public DataSetItemPoint(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }
}
