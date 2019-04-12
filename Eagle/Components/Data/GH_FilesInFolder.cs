using System;
using System.Linq;
using System.Collections.Generic;

using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Grasshopper.Kernel.Geometry;
using Grasshopper.Kernel.Geometry.Delaunay;

namespace Eagle.Components.Data
{
    public class GH_FilesInFolder : GH_Component
    {
        bool recursive = false;
        bool asMesh = false;
        string oldPath = "";
        ConcurrentQueue<Tuple<int, ConcurrentQueue<IGH_GeometricGoo>, FileTypes>> geometry;
        /// <summary>
        /// Initializes a new instance of the AF_GH_DataSetNew class.
        /// </summary>
        public GH_FilesInFolder()
          : base("FilesInFolder", "FiF", "Read files in folder", GH_Categories.Eagle.ToString(), GH_SubCategories.Data.ToString())
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Path", "P", "Path to folder", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Recursive", "R", "Include recursive folder", GH_ParamAccess.item, recursive);
            pManager.AddBooleanParameter("As Mesh", "M", "Create Mesh instead of PointCloud", GH_ParamAccess.item, asMesh);
            pManager.AddIntegerParameter("Point Skip", "S", "# of points to ignore", GH_ParamAccess.item, 1);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Returns geometry", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string path = "";
            int lineSkip = 1;
            if (!DA.GetData(0, ref path)) return;
            DA.GetData(1, ref recursive);
            DA.GetData(2, ref asMesh);
            DA.GetData(3, ref lineSkip);

            if (lineSkip < 1)
            {
                lineSkip = 1;
            }

            try
            {
                if (oldPath != path)
                {
                    oldPath = path;
                    geometry = GetGeometricData(path);
                }
                
                DataTree<IGH_GeometricGoo> geo = new DataTree<IGH_GeometricGoo>();

                foreach (Tuple<int, ConcurrentQueue<IGH_GeometricGoo>, FileTypes>  tuple in geometry)
                {
                    switch (tuple.Item3)
                    {
                        case FileTypes.XYZ:

                            if (asMesh)
                            {
                                ConcurrentQueue<Point3d> pp = new ConcurrentQueue<Point3d>();
                                Parallel.ForEach(tuple.Item2, (item, _, iNum) =>
                                {
                                    if (iNum % lineSkip == 0)
                                    {
                                        GH_Point p = new GH_Point();
                                        if (GH_Convert.ToGHPoint(item, GH_Conversion.Both, ref p))
                                        {
                                            pp.Enqueue(p.Value);
                                        }
                                    }
                                });
                                Mesh mesh = new Mesh();
                                mesh.Vertices.AddVertices(pp);
                                try
                                {
                                    Node2List nodes = new Node2List(pp);
                                    List<Face> faces = Solver.Solve_Faces(nodes, 1);
                                    IEnumerable<MeshFace> meshFaces = faces.Select(x => new MeshFace(x.A, x.B, x.C));
                                    mesh.Faces.AddFaces(meshFaces);

                                    ConcurrentQueue<IGH_GeometricGoo> goo = new ConcurrentQueue<IGH_GeometricGoo>();
                                    goo.Enqueue(GH_Convert.ToGeometricGoo(mesh));
                                    geo.AddRange(goo, new GH_Path(tuple.Item1));
                                }
                                catch (Exception e)
                                {
                                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                                }
                            }
                            else
                            {
                                ConcurrentQueue<IGH_GeometricGoo> goo = new ConcurrentQueue<IGH_GeometricGoo>();
                                Parallel.ForEach(tuple.Item2, (item, _, iNum) =>
                                {
                                    if (iNum % lineSkip == 0)
                                    {
                                        goo.Enqueue(item);
                                    }
                                });
                                geo.AddRange(goo, new GH_Path(tuple.Item1));
                            }
                            break;
                    }
                    
                }
                DA.SetDataTree(0, geo);
            }
            catch (Exception e)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.FilesInFolder;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("1BA667DD-3797-43F2-873C-53FB1F7C5E76"); }
        }


        private ConcurrentQueue<Tuple<int, ConcurrentQueue<IGH_GeometricGoo>, FileTypes>> GetGeometricData(string path)
        {
            ConcurrentQueue<Tuple<int, ConcurrentQueue<IGH_GeometricGoo>, FileTypes>> data = new ConcurrentQueue<Tuple<int, ConcurrentQueue<IGH_GeometricGoo>, FileTypes>>();
            ConcurrentQueue<string> filepaths;
            int gh_Path = 0;
            System.Globalization.CultureInfo EnglishCulture = new System.Globalization.CultureInfo("en-EN");

            if (recursive)
            {
                filepaths = new ConcurrentQueue<string>(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories));
            }
            else
            {
                filepaths = new ConcurrentQueue<string>(Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly));
            }

            Parallel.ForEach(filepaths, file =>
            {
                FileTypes extension = (FileTypes)Enum.Parse(typeof(FileTypes), Path.GetExtension(file).TrimStart('.').ToUpper());
                switch (extension)
                {
                    case FileTypes.XYZ:

                        ConcurrentQueue<IGH_GeometricGoo> goo = new ConcurrentQueue<IGH_GeometricGoo>();

                        Parallel.ForEach(File.ReadLines(file), (line, _, lineNumber) =>
                        {
                            string[] linedata = line.Split(',');
                            if (linedata.Length == 3)
                            {
                                GH_Point p = new GH_Point(new Point3d(Convert.ToDouble(linedata[0], EnglishCulture), Convert.ToDouble(linedata[1], EnglishCulture), Convert.ToDouble(linedata[2], EnglishCulture)));
                                goo.Enqueue(GH_Convert.ToGeometricGoo(p));
                            }
                        });
                        data.Enqueue(new Tuple<int, ConcurrentQueue<IGH_GeometricGoo>, FileTypes>(gh_Path, goo, FileTypes.XYZ));
                        break;

                    default:

                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File '" + file + "' - File type not supported");
                        break;
                }
                gh_Path++;
            });

            return data;
        }
    }
}
