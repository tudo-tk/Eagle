using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Eagle.Components.Symbols
{
    public class GH_Support : GH_Component
    {
        bool constraint_X = false;
        bool constraint_Y = false;
        bool constraint_Z = false;
        bool constraint_RX = false;
        bool constraint_RY = false;
        bool constraint_RZ = false;
        double scale = 1.0;

        /// <summary>
        /// Initializes a new instance of the AF_GH_DataSetNew class.
        /// </summary>
        public GH_Support()
            : base("Support Symbol", "Support", "Displays a support symbol at specified location", GH_Categories.Eagle.ToString(), GH_SubCategories.Symbols.ToString())
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Location", "L", "Location to locate the symbol", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Fix X", "FX", "Toogle to fix x translation", GH_ParamAccess.item, constraint_X);
            pManager.AddBooleanParameter("Fix Y", "FY", "Toogle to fix y translation", GH_ParamAccess.item, constraint_Y);
            pManager.AddBooleanParameter("Fix Z", "FZ", "Toogle to fix z translation", GH_ParamAccess.item, constraint_Z);
            pManager.AddBooleanParameter("Fix RX", "RX", "Toogle to fix rotation around x axis", GH_ParamAccess.item, constraint_RX);
            pManager.AddBooleanParameter("Fix RY", "RY", "Toogle to fix rotation around y axis", GH_ParamAccess.item, constraint_RY);
            pManager.AddBooleanParameter("Fix RZ", "RZ", "Toogle to fix rotation around z axis", GH_ParamAccess.item, constraint_RZ);
            pManager.AddNumberParameter("Scale", "S", "Parameter to scale the symbol", GH_ParamAccess.item, scale);

            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Support", "S", "Support symbol as geometry", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<IGH_GeometricGoo> geo = new List<IGH_GeometricGoo>();

            if (!DA.GetDataList(0, geo)) return;
            if (!DA.GetData(1, ref constraint_X)) return;
            if (!DA.GetData(2, ref constraint_Y)) return;
            if (!DA.GetData(3, ref constraint_Z)) return;
            if (!DA.GetData(4, ref constraint_RX)) return;
            if (!DA.GetData(5, ref constraint_RY)) return;
            if (!DA.GetData(6, ref constraint_RZ)) return;
            DA.GetData(7, ref scale);


            DataTree<IGH_GeometricGoo> symbols = new DataTree<IGH_GeometricGoo>();

            for (int i = 0; i < geo.Count; i++)
            {
                Point3d p = new Point3d();

                if (geo[i] is Point3d)
                {
                    if (!GH_Convert.ToPoint3d(geo[i], ref p, GH_Conversion.Both))
                    {
                        return;
                    }
                }
                else if (geo[i] is GH_Point)
                {
                    GH_Point ghP = new GH_Point();

                    if (GH_Convert.ToGHPoint(geo[i], GH_Conversion.Both, ref ghP))
                    {
                        p = ghP.Value;
                    }
                    else
                    {
                        return;
                    }
                }

                double radius = scale / 8.0;

                Sphere s1 = new Sphere(p, radius);

                double pX = radius * Math.Sin(Math.PI / 4) * Math.Cos(Math.PI / 4);
                double pY = radius * Math.Sin(Math.PI / 4) * Math.Sin(Math.PI / 4);
                double pZ = radius * Math.Cos(Math.PI / 4);

                double pX0 = radius * Math.Sin(Math.PI / 4) * Math.Cos(0);
                double pY0 = radius * Math.Sin(0) * Math.Sin(Math.PI / 4);

                Point3d p01 = new Point3d(p.X - pX0, p.Y - pY0, p.Z - pZ);
                Point3d p02 = new Point3d(p.X + pX0, p.Y - pY0, p.Z - pZ);
                Point3d p03 = new Point3d(p.X + pX0, p.Y + pY0, p.Z - pZ);
                Point3d p04 = new Point3d(p.X - pX0, p.Y + pY0, p.Z - pZ);

                Point3d p11 = new Point3d(p.X - pX, p.Y - pY, p.Z - pZ);
                Point3d p12 = new Point3d(p.X + pX, p.Y - pY, p.Z - pZ);
                Point3d p13 = new Point3d(p.X + pX, p.Y + pY, p.Z - pZ);
                Point3d p14 = new Point3d(p.X - pX, p.Y + pY, p.Z - pZ);

                Point3d p21 = new Point3d(p.X - scale / 2.0, p.Y - scale / 2.0, p.Z - scale / 2.0);
                Point3d p22 = new Point3d(p.X + scale / 2.0, p.Y - scale / 2.0, p.Z - scale / 2.0);
                Point3d p23 = new Point3d(p.X + scale / 2.0, p.Y + scale / 2.0, p.Z - scale / 2.0);
                Point3d p24 = new Point3d(p.X - scale / 2.0, p.Y + scale / 2.0, p.Z - scale / 2.0);

                Line l1 = new Line(p11, p21);
                Line l2 = new Line(p12, p22);
                Line l3 = new Line(p13, p23);
                Line l4 = new Line(p14, p24);

                Line l5 = new Line(p21, p22);
                Line l6 = new Line(p22, p23);
                Line l7 = new Line(p23, p24);
                Line l8 = new Line(p24, p21);

                Arc a1 = new Arc(p11, p01, p12);
                Arc a2 = new Arc(p12, p02, p13);
                Arc a3 = new Arc(p13, p03, p14);
                Arc a4 = new Arc(p14, p04, p11);

                List<Curve> c1 = new List<Curve>() { l1.ToNurbsCurve(), l2.ToNurbsCurve(), l5.ToNurbsCurve(), a3.ToNurbsCurve() };
                List<Curve> c2 = new List<Curve>() { l2.ToNurbsCurve(), l3.ToNurbsCurve(), l6.ToNurbsCurve(), a2.ToNurbsCurve() };
                List<Curve> c3 = new List<Curve>() { l3.ToNurbsCurve(), l4.ToNurbsCurve(), l7.ToNurbsCurve(), a1.ToNurbsCurve() };
                List<Curve> c4 = new List<Curve>() { l4.ToNurbsCurve(), l1.ToNurbsCurve(), l8.ToNurbsCurve(), a4.ToNurbsCurve() };

                Brep b1 = Brep.CreateEdgeSurface(c1);
                Brep b2 = Brep.CreateEdgeSurface(c2);
                Brep b3 = Brep.CreateEdgeSurface(c3);
                Brep b4 = Brep.CreateEdgeSurface(c4);

                symbols.Add(GH_Convert.ToGeometricGoo(s1), new GH_Path(i));

                symbols.Add(GH_Convert.ToGeometricGoo(l1), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(l2), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(l3), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(l4), new GH_Path(i));

                symbols.Add(GH_Convert.ToGeometricGoo(l5), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(l6), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(l7), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(l8), new GH_Path(i));

                symbols.Add(GH_Convert.ToGeometricGoo(b1), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(b2), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(b3), new GH_Path(i));
                symbols.Add(GH_Convert.ToGeometricGoo(b4), new GH_Path(i));
            }
            DA.SetDataTree(0, symbols);
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
            get { return new Guid("D605CD59-A9EB-4163-BAE4-0AC89E042ECF"); }
        }
    }
}
