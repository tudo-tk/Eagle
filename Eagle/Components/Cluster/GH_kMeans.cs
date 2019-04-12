using System;
using System.Linq;
using System.Collections.Generic;

using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

using Accord.MachineLearning;
using Eagle.Utils;

namespace Eagle.Components.Cluster
{
    public class GH_kMeans : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AF_GH_DataSetNew class.
        /// </summary>
        public GH_kMeans()
            : base("k-Means", "k-Means", "Lloyd's k-Means clustering algorithm.", GH_Categories.Eagle.ToString(), GH_SubCategories.Cluster.ToString())
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "D", "Data to make cluster as points or double values", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Geometry", "G", "Geometry to be clustered", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Num Clusters", "C", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Cluster Data", "CD", "Returns sets of clusters", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Cluster Geometry", "CG", "Returns sets of clusters", GH_ParamAccess.tree);
            pManager.AddGeometryParameter("Cluster Centroids", "CC", "Returns the centroids of each cluster", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int i, j, k;
            bool IsPointData = false;

            GH_Structure<IGH_Goo> data = new GH_Structure<IGH_Goo>();
            GH_Structure<IGH_GeometricGoo> geo = new GH_Structure<IGH_GeometricGoo>();
            List<int> numCluster = new List<int>();

            if (!DA.GetDataTree(0, out data)) return;
            if (!DA.GetDataTree(1, out geo)) return;
            if (!DA.GetDataList(2, numCluster)) return;

            data.Simplify(GH_SimplificationMode.CollapseAllOverlaps);
            DataTree<IGH_Goo> outputData = new DataTree<IGH_Goo>();
            DataTree<IGH_GeometricGoo> outputGeo = new DataTree<IGH_GeometricGoo>();
            DataTree<Point3d> outputCentroids = new DataTree<Point3d>();

            for (i = 0; i < data.Branches.Count; i++)
            {
                double[] x = new double[data.Branches[i].Count];
                double[] y = new double[data.Branches[i].Count];
                double[] z = new double[data.Branches[i].Count];

                for (j = 0; j < data.Branches[i].Count; j++)
                {
                    if (data.Branches[i][j] is GH_Point)
                    {
                        IsPointData = true;
                        GH_Point target = new GH_Point();

                        if (GH_Convert.ToGHPoint(data.Branches[i][j], GH_Conversion.Both, ref target))
                        {
                            x[j] = target.Value.X;
                            y[j] = target.Value.Y;
                            z[j] = target.Value.Z;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                if (IsPointData)
                {
                    List<double[]> datalist = new List<double[]>
                {
                    x,
                    y,
                    z
                };
                    double[][] _data = ArrayConvert.To2DArray(datalist);

                    KMeans m = new KMeans(numCluster[i]);
                    KMeansClusterCollection cluster = m.Learn(_data);

                    int[] labels = cluster.Decide(_data);
                    double[][] centroids = m.Centroids;

                    for (j = 0; j < data.Branches[i].Count; j++)
                    {
                        GH_Path path = new GH_Path(i, labels[j]);
                        outputData.Add(data.Branches[i][j], path);
                        outputGeo.Add(geo.Branches[i][j], path);
                    }

                    for (k = 0; k < centroids.Length; k++)
                    {
                        outputCentroids.Add(new Point3d(centroids.ElementAt(k).ElementAt(0), centroids.ElementAt(k).ElementAt(1), centroids.ElementAt(k).ElementAt(2)), new GH_Path(k));
                    }
                }
                else
                {
                    break;
                }
            }

            if (!IsPointData)
            {
                GH_Path oldPath = new GH_Path();
                GH_Path newPath = new GH_Path();
                int DataGroupCount = 0;

                for (i = 0; i < data.PathCount; i++)
                {
                    if (data.Paths[i].Indices.Length == 1)
                    {
                        DataGroupCount = 1;
                        break;
                    }
                    else
                    {
                        int[] pp = new int[data.Paths[i].Indices.Length - 1];

                        for (j = 0; j < data.Paths[i].Indices.Length - 1; j++)
                        {
                            pp[j] = data.Paths[i].Indices[j];
                        }
                        newPath.Indices = pp;

                        if (newPath != oldPath)
                        {
                            DataGroupCount++;
                            oldPath = newPath;
                        }
                        newPath = new GH_Path();
                    }
                }

                for (i = 0; i < DataGroupCount; i++)
                {
                    List<double[]> datalist = new List<double[]>();

                    for (j = 0; j < data.Branches.Count / DataGroupCount; j++)
                    {
                        double[] values = new double[data.Branches[DataGroupCount * i + j].Count];

                        for (k = 0; k < data.Branches[DataGroupCount * i + j].Count; k++)
                        {
                            if (data.Branches[DataGroupCount * i + j][k] is GH_Number)
                            {
                                if (GH_Convert.ToDouble(data.Branches[DataGroupCount * i + j][k], out double value, GH_Conversion.Both))
                                {
                                    values[k] = value;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                        datalist.Add(values);
                    }
                    double[][] _data = ArrayConvert.ToDoubleArray(datalist);

                    KMeans m = new KMeans(numCluster[0]);
                    KMeansClusterCollection cluster = m.Learn(_data);

                    int[] labels = cluster.Decide(_data);

                    for (j = 0; j < labels.Length; j++)
                    {
                        List<IGH_Goo> numbers = new List<IGH_Goo>();
                        List<IGH_GeometricGoo> geos = new List<IGH_GeometricGoo>();

                        for (k = 0; k < data.Branches[DataGroupCount * i + j].Count; k++)
                        {
                            numbers.Add(data.Branches[DataGroupCount * i + j][k]);
                            geos.Add(geo.Branches[DataGroupCount * i + j][k]);
                        }

                        GH_Path path = new GH_Path(i, j, labels[j]);
                        outputData.AddRange(numbers, path);
                        outputGeo.AddRange(geos, path);
                    }
                }
            }
            DA.SetDataTree(0, outputData);
            DA.SetDataTree(1, outputGeo);
            DA.SetDataTree(2, outputCentroids);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.kMeans;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("A91978FE-AE54-4EAB-B8B3-7CA4C40095B4"); }
        }
    }
}
