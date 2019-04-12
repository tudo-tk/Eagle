using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

using Eagle.Analysis.Cluster;

namespace Eagle.Components.Cluster
{
    public class GH_DbScan : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AF_GH_DataSetNew class.
        /// </summary>
        public GH_DbScan()
          : base("Dbscan", "Dbscan", "Density-based spatial clustering of applications with noise.", GH_Categories.Eagle.ToString(), GH_SubCategories.Cluster.ToString())
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "D", "Data to be clustered as points or double values", GH_ParamAccess.tree);
            pManager.AddNumberParameter("epsilon", "ɛ", "Desired region ball radius", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Minimun points", "minP", "Minimum number of points to be in a region", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Clusters", "C", "Returns sets of clusters, renew the parameter", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool IsPointData = false;

            GH_Structure<IGH_Goo> data = new GH_Structure<IGH_Goo>();
            List<double> eps = new List<double>();
            List<int> minP = new List<int>();

            if (!DA.GetDataTree(0, out data)) return;
            if (!DA.GetDataList(1, eps)) return;
            if (!DA.GetDataList(2, minP)) return;

            data.Simplify(GH_SimplificationMode.CollapseAllOverlaps);

            List<DataSetItemPoint[]> points = new List<DataSetItemPoint[]>();

            for (int i = 0; i < data.Branches.Count; i++)
            {
                DataSetItemPoint[] pp = new DataSetItemPoint[data.Branches[i].Count];

                for (int j = 0; j < data.Branches[i].Count; j++)
                {
                    if (data.Branches[i][j] is GH_Point)
                    {
                        IsPointData = true;
                        GH_Point target = new GH_Point();
                        if (GH_Convert.ToGHPoint(data.Branches[i][j], GH_Conversion.Both, ref target))
                        {
                            pp[j] = new DataSetItemPoint(target.Value.X, target.Value.Y, target.Value.Z, 0.0);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                if (IsPointData)
                {
                    points.Add(pp);
                }
                else
                {
                    break;
                }
            }

            // double data
            if (!IsPointData)
            {
                DataSetItemPoint[] pp = new DataSetItemPoint[data.Branches.Count];

                for (int i = 0; i < data.Branches.Count; i++)
                {
                    DataSetItemPoint p = new DataSetItemPoint();
                    for (int j = 0; j < data.Branches[i].Count; j++)
                    {
                        if (data.Branches[i][j] is GH_Number)
                        {
                            if (GH_Convert.ToDouble(data.Branches[i][j], out double value, GH_Conversion.Both))
                            {
                                switch (j)
                                {
                                    case 0:
                                        p.X = value;
                                        break;

                                    case 1:
                                        p.Y = value;
                                        break;

                                    case 2:
                                        p.Z = value;
                                        break;

                                    case 3:
                                        p.W = value;
                                        break;
                                }
                            }
                        }
                    }
                    pp[i] = p;
                }
                points.Add(pp);
            }

            if (IsPointData)
            {
                DataTree<IGH_Goo> output = new DataTree<IGH_Goo>();

                for (int i = 0; i < points.Count; i++)
                {
                    DbscanAlgorithm<DataSetItemPoint> dbs = new DbscanAlgorithm<DataSetItemPoint>((x, y) => Math.Sqrt(((x.X - y.X) * (x.X - y.X)) + ((x.Y - y.Y) * (x.Y - y.Y)) + ((x.Z - y.Z) * (x.Z - y.Z)) + ((x.W - y.W) * (x.W - y.W))));
                    dbs.ComputeClusterDbscan(points[i].ToArray(), eps[i], minP[i], out HashSet<DataSetItemPoint[]> clusters3d);

                    for (int j = 0; j < clusters3d.Count; j++)
                    {
                        ConcurrentQueue<GH_Point> _points = new ConcurrentQueue<GH_Point>();
                        Parallel.ForEach(clusters3d.ElementAt(j), p =>
                        {
                            _points.Enqueue(new GH_Point(new Point3d(p.X, p.Y, p.Z)));
                        });

                        output.AddRange(_points.ToList(), new GH_Path(i, j));
                    }
                }

                DA.SetDataTree(0, output);
            }
            else
            {
                DataTree<GH_Number> output = new DataTree<GH_Number>();

                for (int i = 0; i < points.Count; i++)
                {
                    DbscanAlgorithm<DataSetItemPoint> dbs = new DbscanAlgorithm<DataSetItemPoint>((x, y) => Math.Sqrt(((x.X - y.X) * (x.X - y.X)) + ((x.Y - y.Y) * (x.Y - y.Y)) + ((x.Z - y.Z) * (x.Z - y.Z)) + ((x.W - y.W) * (x.W - y.W))));
                    dbs.ComputeClusterDbscan(points[i].ToArray(), eps[i], minP[i], out HashSet<DataSetItemPoint[]> clusters3d);

                    for (int j = 0; j < clusters3d.Count; j++)
                    {
                        ConcurrentQueue<List<double>> _points = new ConcurrentQueue<List<double>>();

                        for (int k = 0; k < clusters3d.ElementAt(j).Length; k++)
                        {
                            List<GH_Number> ii = new List<GH_Number>();
                            GH_Number target1 = new GH_Number();
                            GH_Number target2 = new GH_Number();
                            GH_Number target3 = new GH_Number();
                            GH_Number target4 = new GH_Number();
                            if (GH_Convert.ToGHNumber(clusters3d.ElementAt(j).ElementAt(k).X, GH_Conversion.Both, ref target1))
                            {
                                ii.Add(target1);
                            }
                            if (GH_Convert.ToGHNumber(clusters3d.ElementAt(j).ElementAt(k).Y, GH_Conversion.Both, ref target2))
                            {
                                ii.Add(target2);
                            }
                            if (GH_Convert.ToGHNumber(clusters3d.ElementAt(j).ElementAt(k).Z, GH_Conversion.Both, ref target3))
                            {
                                ii.Add(target3);
                            }
                            if (GH_Convert.ToGHNumber(clusters3d.ElementAt(j).ElementAt(k).W, GH_Conversion.Both, ref target4))
                            {
                                ii.Add(target4);
                            }

                            output.AddRange(ii, new GH_Path(i, j, k));
                        }
                    }
                }
                DA.SetDataTree(0, output);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.DbScan;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("BA1B7D4D-502A-4E01-8421-0CD6561E9133"); }
        }
    }
}
