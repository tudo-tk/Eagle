using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using Rhino.Geometry;
using Rhino.Display;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;


namespace Eagle.Components.Cluster
{
    public class GH_CustomDisplay : GH_Component
    {
        private CustomDisplay m_display = new CustomDisplay(true);
        /// <summary>
        /// Initializes a new instance of the AF_GH_DataSetNew class.
        /// </summary>
        public GH_CustomDisplay()
          : base("Custom Display", "Custom Display", "Custom display of grasshopper geometry", GH_Categories.Eagle.ToString(), GH_SubCategories.Data.ToString())
        {
            this.ObjectChanged += new IGH_DocumentObject.ObjectChangedEventHandler(OnAttributesChanged);
        }

        private void OnAttributesChanged(object sender, GH_ObjectChangedEventArgs e)
        {
            switch (e.Type)
            {
                case GH_ObjectEventType.Preview:

                    if (this.Hidden)
                    {
                        m_display.Clear();
                    }
                    else
                    {
                        this.ExpireSolution(true);
                    }
                    break;
            }
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geoemtry", "G", "Geoemtry", GH_ParamAccess.tree);
            pManager.AddColourParameter("Color", "C", "Color for geometry", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Style", "DS", "Rhino Display Style", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Size", "S", "Size of geometry", GH_ParamAccess.item);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            //pManager.AddGeometryParameter("Geometry", "G", "Returns geometry", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!this.Hidden)
            {
                try
                {
                    m_display.Clear();
                }
                catch
                {
                    m_display = new CustomDisplay(true);
                }
                GH_Structure<IGH_GeometricGoo> geo = new GH_Structure<IGH_GeometricGoo>();
                List<Color> colors = new List<Color>();
                int style = 4;
                int size = 1;

                if (!DA.GetDataTree(0, out geo)) return;
                if (!DA.GetDataList(1, colors)) return;
                DA.GetData(2, ref style);
                DA.GetData(3, ref size);
                try
                {
                    geo.Simplify(GH_SimplificationMode.CollapseAllOverlaps);

                    if (geo.Branches.Count == colors.Count)
                    {
                        for (int i = 0; i < geo.Branches.Count; i++)
                        {
                            ConcurrentQueue<Point3d> points = new ConcurrentQueue<Point3d>();

                            // Testing first object
                            if (geo.Branches[i][0] is GH_Point)
                            {
                                Parallel.ForEach(geo.Branches[i], obj =>
                                {
                                    if (obj is GH_Point)
                                    {
                                        GH_Point p = new GH_Point();
                                        if (GH_Convert.ToGHPoint(obj, GH_Conversion.Both, ref p))
                                        {
                                            points.Enqueue(new Point3d(p.Value.X, p.Value.Y, p.Value.Z));
                                        }
                                    }
                                });
                            }
                            // Testing for mesh
                            else if (geo.Branches[i][0].TypeName == "Mesh")
                            {
                                Parallel.ForEach(geo.Branches[i], obj =>
                                {
                                    if (obj.TypeName == "Mesh")
                                    {
                                        Mesh m = new Mesh();
                                        if (GH_Convert.ToMesh(obj, ref m, GH_Conversion.Both))
                                        {
                                            points = new ConcurrentQueue<Point3d>(m.Vertices.ToPoint3dArray());
                                        }
                                    }
                                });
                            }

                            PointStyle pointStyle = (PointStyle)style;
                            m_display.AddPoints(points, colors[i], pointStyle, size);

                        }
                    }
                    else
                    {
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Data input incorrect!");
                    }

                }
                catch (Exception e)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                }
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon
        {
            get
            {
                return Properties.Resources.CustomDisplay;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("1687A49F-4AB9-420B-B1B2-297C253DBC56"); }
        }


        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);
            this.ObjectChanged -= new IGH_DocumentObject.ObjectChangedEventHandler(OnAttributesChanged);
            m_display.Dispose();
        }
    }
}
