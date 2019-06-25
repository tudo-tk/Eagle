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


// -----------------------------------------------------------------
// Elastic Bending Script by Will McElwain
// Created February 2014
// 
// DESCRIPTION:
// This beast creates the so-called 'elastica curve', the shape a long, thin rod or wire makes when it is bent elastically (i.e. not permanently). In this case, force
// is assumed to only be applied horizontally (which would be in line with the rod at rest) and both ends are assumed to be pinned or hinged meaning they are free
// to rotate (as opposed to clamped, when the end tangent angle is fixed, usually horizontally). An interesting finding is that it doesn't matter what the material or
// cross-sectional area is, as long as they're uniform along the entire length. Everything makes the same shape when bent as long as it doesn't cross the threshold
// from elastic to plastic (permanent) deformation (I don't bother to find that limit here, but can be found if the yield stress for a material is known).
// 
// Key to the formulas used in this script are elliptic integrals, specifically K(m), the complete elliptic integral of the first kind, and E(m), the complete elliptic
// integral of the second kind. There was a lot of confusion over the 'm' and 'k' parameters for these functions, as some people use them interchangeably, but they are
// not the same. m = k^2 (thus k = Sqrt(m)). I try to use the 'm' parameter exclusively to avoid this confusion. Note that there is a unique 'm' parameter for every
// configuration/shape of the elastica curve.
// 
// This script tries to find that unique 'm' parameter based on the inputs. The algorithm starts with a test version of m, evaluates an expression, say 2*E(m)/K(m)-1,
// then compares the result to what it should be (in this case, a known width/length ratio). Iterate until the correct m is found. Once we have m, we can then calculate
// all of the other unknowns, then find points that lie on that curve, then interpolate those points for the actual curve. You can also use Wolfram|Alpha as I did to
// find the m parameter based on the equations in this script (example here: http://tiny.cc/t4tpbx for when say width=45.2 and length=67.1).
// 
// Other notes:
// * This script works with negative values for width, which will creat a self-intersecting curve (as it should). The curvature of the elastica starts to break down around
// m=0.95 (~154°), but this script will continue to work until M_MAX, m=0.993 (~169°). If you wish to ignore self-intersecting curves, set ignoreSelfIntersecting to True
// * When the only known values are length and height, it is actually possible for certain ratios of height to length to have two valid m values (thus 2 possible widths
// and angles). This script will return them both.
// * Only the first two valid parameters (of the required ones) will be used, meaning if all four are connected (length, width or a PtB, height, and angle), this script will
// only use length and width (or a PtB).
// * Depending on the magnitude of your inputs (say if they're really small, like if length < 10), you might have to increase the constant ROUNDTO at the bottom
// 
// REFERENCES:
// {1} "The elastic rod" by M.E. Pacheco Q. & E. Pina, http://www.scielo.org.mx/pdf/rmfe/v53n2/v53n2a8.pdf
// {2} "An experiment in nonlinear beam theory" by A. Valiente, http://www.deepdyve.com/lp/doc/I3lwnxdfGz , also here: http://tiny.cc/Valiente_AEiNBT
// {3} "Snap buckling, writhing and Loop formation In twisted rods" by V.G.A. GOSS, http://myweb.lsbu.ac.uk/~gossga/thesisFinal.pdf
// {4} "Theory of Elastic Stability" by Stephen Timoshenko, http://www.scribd.com/doc/50402462/Timoshenko-Theory-of-Elastic-Stability  (start on p. 76)
// 
// INPUT:
// PtA - First anchor point (required)
// PtB - Second anchor point (optional, though 2 out of the 4--length, width, height, angle--need to be specified)
// [note that PtB can be the same as PtA (meaning width would be zero)]
// [also note that if a different width is additionally specified that's not equal to the distance between PtA and PtB, then the end point will not equal PtB anymore]
// Pln - Plane of the bent rod/wire, which bends up in the +y direction. The line between PtA and PtB (if specified) must be parallel to the x-axis of this plane
// 
// ** 2 of the following 4 need to be specified **
// Len - Length of the rod/wire, which needs to be > 0
// Wid - Width between the endpoints of the curve [note: if PtB is specified in addition, and distance between PtA and PtB <> width, the end point will be relocated
// Ht - Height of the bent rod/wire (when negative, curve will bend downward, relative to the input plane, instead)
// Ang - Inner departure angle or tangent angle (in radians) at the ends of the bent rod/wire. Set up so as width approaches length (thus height approaches zero), angle approaches zero
// 
// * Following variables only needed for optional calculating of bending force, not for shape of curve.
// E - Young's modulus (modulus of elasticity) in GPa (=N/m^2) (material-specific. for example, 7075 aluminum is roughly 71.7 GPa)
// I - Second moment of area (or area moment of inertia) in m^4 (cross-section-specific. for example, a hollow rod
// would have I = pi * (outer_diameter^4 - inner_diameter^4) / 32
// Note: E*I is also known as flexural rigidity or bending stiffness
// 
// OUTPUT:
// out - only for debugging messages
// Pts - the list of points that approximate the shape of the elastica
// Crv - the 3rd-degree curve interpolated from those points (with accurate start & end tangents)
// L - the length of the rod/wire
// W - the distance (width) between the endpoints of the rod/wire
// H - the height of the bent rod/wire
// A - the tangent angle at the (start) end of the rod/wire
// F - the force needed to hold the rod/wire in a specific shape (based on the material properties & cross-section) **be sure your units for 'I' match your units for the
// rest of your inputs (length, width, etc.). Also note that the critical buckling load (force) that makes the rod/wire start to bend can be found at height=0
// 
// THANKS TO:
// Mårten Nettelbladt (thegeometryofbending.blogspot.com)
// Daniel Piker (Kangaroo plugin)
// David Rutten (Grasshopper guru)
// Euler & Bernoulli (the O.G.'s)
// 
// -----------------------------------------------------------------


namespace Eagle.Components.Cluster
{
    public class GH_Elastica : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the AF_GH_DataSetNew class.
        /// </summary>
        public GH_Elastica()
          : base("Elastica", "Elastica", "Density-based spatial clustering of applications with noise.", GH_Categories.Eagle.ToString(), GH_SubCategories.Physics.ToString())
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Start point A", "PtA", "Start point A", GH_ParamAccess.item);
            pManager.AddPointParameter("Start point B", "PtB", "Start point B", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Working plane", "P", "Working plane of elastica", GH_ParamAccess.item);
            pManager.AddNumberParameter("Length", "L", "Curve length", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "W", "Distance between point A and B", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height", "H", "Height of curve", GH_ParamAccess.item);
            pManager.AddNumberParameter("Angle", "A", "Angle", GH_ParamAccess.item);
            pManager.AddNumberParameter("E modulus", "E", "Elastic modulus of curve", GH_ParamAccess.item);
            pManager.AddNumberParameter("Moment of inertia", "I", "Moment of inertia of curve", GH_ParamAccess.item);

            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "Pts", "Resulting points", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Curve", "C", "Elastica", GH_ParamAccess.list);
            pManager.AddNumberParameter("Length", "L", "Curve length", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "W", "Distance between point A and B", GH_ParamAccess.list);
            pManager.AddNumberParameter("Height", "H", "Height of curve", GH_ParamAccess.item);
            pManager.AddNumberParameter("Angle", "A", "Angle", GH_ParamAccess.list);
            pManager.AddNumberParameter("Angle", "F", "etbne", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool ignoreSelfIntersecting = false;  // set to True if you don't want to output curves where width < 0, which creates a self-intersecting curve
            int inCt = 0;  // count the number of required parameters that are receiving data

            Point3d PtA = new Point3d();
            Point3d PtB = new Point3d();
            Plane refPlane = new Plane();
            double W = 0.0;
            double width = double.NaN;
            double length = 0.0;
            double height = 0.0;
            double angle = 0.0;
            double m = 0.0;
            List<double> multiple_m = new List<double>();
            bool flip_H = false;  // if height is negative, this flag will be set
            bool flip_A = false;  // if angle is negative, this flag will be set

            double E = 0.0;
            double I = 0.0;

            if (!DA.GetData(0, ref PtA)) return;
            
            if (!DA.GetData(2, ref refPlane))
            {
                refPlane = Plane.WorldXY;
                refPlane.Origin = PtA;
            }
            // Points to be in refPlane
            if (Math.Round(refPlane.DistanceTo(PtA), Defined.ROUNDTO) != 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Point A is not on the base plane");
                return;
            }
            if (DA.GetData(1, ref PtB))
            {
                // Points to be in refPlane
                if (Math.Round(refPlane.DistanceTo(PtB), Defined.ROUNDTO) != 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Point B is not on the base plane");
                    return;
                }
                Line AtoB = new Line(PtA, PtB);
                if (AtoB.Length != 0 & !AtoB.Direction.IsPerpendicularTo(refPlane.YAxis))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The line between PtA and PtB is not perpendicular to the Y-axis of the specified plane");
                    return;
                }
                inCt++;
                if (DA.GetData(4, ref W))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Width W will override the distance between PtA and PtB. If you do not want this to happen, disconnect PtB or Width.");
                }
                W = PtA.DistanceTo(PtB);  // get the width (distance) between PtA and PtB

                Point3d refPtB;
                if (refPlane.RemapToPlaneSpace(PtB, out refPtB))
                {
                    if (refPtB.X < 0)
                    {
                        W = -W;  // check if PtB is to the left of PtA...if so, width is negative
                    }
                }
                width = W;
            }

            if (DA.GetData(3, ref length))
            {
                inCt++;
            }
            if (DA.GetData(4, ref W))
            {
                inCt++;
            }
            if (DA.GetData(5, ref height))
            {
                inCt++;
            }
            if (DA.GetData(6, ref angle))
            {
                inCt++;
            }

            if (inCt > 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "More parameters set than are required (out of length, width, height, angle). Only using the first two valid ones.");
            }

            if (DA.GetData(3, ref length))
            {
                if (length <= 0.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Length cannot be negative or zero");
                    return;
                }
                if (DA.GetData(4, ref W))
                {
                    if (W > length)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width is greater than length");
                        return;
                    }
                    if (W == length)
                    {
                        height = 0;
                        m = 0;
                        angle = 0;
                        width = W;
                    }
                    else
                    {
                        m = SolveMFromLengthWidth(length, W);
                        height = Cal_H(length, m);  // L * Sqrt(m) / K(m)
                        angle = Cal_A(m);  // Acos(1 - 2 * m)
                        width = W;
                    }
                }
                else if (W != double.NaN)
                {
                    if (W > length)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width is greater than length");
                        return;
                    }
                    if (W == length)
                    {
                        height = 0;
                        m = 0;
                        angle = 0;
                    }
                    else
                    {
                        m = SolveMFromLengthWidth(length, W);
                        height = Cal_H(length, m);  // L * Sqrt(m) / K(m)
                        angle = Cal_A(m);  // Acos(1 - 2 * m)
                    }
                    width = W;
                }
                else if (DA.GetData(5, ref height))
                {
                    if (Math.Abs(height / length) > Defined.MAX_HL_RATIO)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Height not possible with given length");
                        return;
                    }
                    if (height < 0)
                    {
                        height = -height;  // if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_A = true;
                        flip_H = true;
                    }
                    if (height == 0)
                    {
                        width = length;
                        angle = 0;
                    }
                    else
                    {
                        multiple_m = SolveMFromLengthHeight(length, height);  // note that it's possible for two values of m to be found if height is close to max height
                        if (multiple_m.Count == 1)
                        {
                            m = multiple_m.ElementAt(0);
                            width = Cal_W(length, m);  // L * (2 * E(m) / K(m) - 1)
                            angle = Cal_A(m);  // Acos(1 - 2 * m)
                        }
                    }
                }
                else if (DA.GetData(6, ref angle))
                {
                    if (angle < 0)
                    {
                        angle = -angle;  // if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_A = true;
                        flip_H = true;
                    }
                    m = Cal_M(angle);  // (1 - Cos(a)) / 2
                    if (angle == 0)
                    {
                        width = length;
                        height = 0;
                    }
                    else
                    {
                        width = Cal_W(length, m);  // L * (2 * E(m) / K(m) - 1)
                        height = Cal_H(length, m);  // L * Sqrt(m) / K(m)
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need to specify one more parameter in addition to length");
                    return;
                }
            }
            else if (DA.GetData(4, ref W))
            {
                if (DA.GetData(5, ref height))
                {
                    if (height < 0)
                    {
                        height = -height;  // if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_A = true;
                        flip_H = true;
                    }
                    if (height == 0)
                    {
                        length = W;
                        angle = 0;
                    }
                    else
                    {
                        m = SolveMFromWidthHeight(W, height);
                        length = Cal_L(height, m);  // h * K(m) / Sqrt(m)
                        angle = Cal_A(m);  // Acos(1 - 2 * m)
                    }
                }
                else if (DA.GetData(6, ref angle))
                {
                    if (W == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve not possible with width = 0 and an angle as inputs");
                        return;
                    }
                    if (angle < 0)
                    {
                        angle = -angle;  // if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_A = true;
                        flip_H = true;
                    }
                    m = Cal_M(angle);  // (1 - Cos(a)) / 2
                    if (angle == 0)
                    {
                        length = W;
                        height = 0;
                    }
                    else
                    {
                        length = W / (2 * EllipticE(m) / EllipticK(m) - 1);
                        if (length < 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve not possible at specified width and angle (calculated length is negative)");
                            return;
                        }
                        height = Cal_H(length, m);  // L * Sqrt(m) / K(m)
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need to specify one more parameter in addition to width (Wid)");
                    return;
                }
                width = W;
            }
            else if (width != double.NaN)
            {
                if (DA.GetData(5, ref height))
                {
                    if (height < 0)
                    {
                        height = -height;  // if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_A = true;
                        flip_H = true;
                    }
                    if (height == 0)
                    {
                        length = width;
                        angle = 0;
                    }
                    else
                    {
                        m = SolveMFromWidthHeight(width, height);
                        length = Cal_L(height, m);  // h * K(m) / Sqrt(m)
                        angle = Cal_A(m);  // Acos(1 - 2 * m)
                    }
                }
                else if (DA.GetData(6, ref angle))
                {
                    if (width == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve not possible with width = 0 and an angle as inputs");
                        return;
                    }
                    if (angle < 0)
                    {
                        angle = -angle;  // if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_A = true;
                        flip_H = true;
                    }
                    m = Cal_M(angle);  // (1 - Cos(a)) / 2
                    if (angle == 0)
                    {
                        length = width;
                        height = 0;
                    }
                    else
                    {
                        length = width / (2 * EllipticE(m) / EllipticK(m) - 1);
                        if (length < 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve not possible at specified width and angle (calculated length is negative)");
                            return;
                        }
                        height = Cal_H(length, m);  // L * Sqrt(m) / K(m)
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel. Error, "Need to specify one more parameter in addition to PtA and PtB");
                    return;
                }
            }
            else if (DA.GetData(5, ref height))
            {
                if (DA.GetData(6, ref angle))
                {
                    if (height < 0)
                    {
                        height = -height;  // if height is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                        refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                        flip_H = true;
                        flip_A = true;
                    }
                    if (height == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Height can't = 0 if only height and angle are specified");
                        return;
                    }
                    else
                    {
                        if (angle < 0)
                        {
                            angle = -angle;  // if angle is negative, set it to positive (for the calculations) but flip the reference plane about its x-axis
                            refPlane.Transform(Transform.Mirror(new Plane(refPlane.Origin, refPlane.XAxis, refPlane.ZAxis)));
                            flip_A = !flip_A;
                            flip_H = !flip_H;
                        }
                        m = Cal_M(angle);  // (1 - Cos(a)) / 2
                        if (angle == 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Angle can't = 0 if only height and angle are specified");
                            return;
                        }
                        else
                        {
                            length = Cal_L(height, m);  // h * K(m) / Sqrt(m)
                            width = Cal_W(length, m);  // L * (2 * E(m) / K(m) - 1)
                        }
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need to specify one more parameter in addition to height");
                    return;
                }
            }
            else if (DA.GetData(6, ref angle))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need to specify one more parameter in addition to angle");
                return;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Need to specify two of the four parameters: length, width (or PtB), height, and angle");
                return;
            }

            if (m > Defined.M_MAX)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Form of curve not solvable with current algorithm and given inputs");
                return;
            }

            refPlane.Origin = refPlane.PointAt(width / (double)2, 0, 0);  // adjust the origin of the reference plane so that the curve is centered about the y-axis (start of the curve is at x = -width/2)

            DA.GetData(7, ref E);

            if (multiple_m.Count > 1)
            {
                DataTree<Point3d> multi_pts = new DataTree<Point3d>();
                List<Curve> multi_crv = new List<Curve>();
                List<Point3d> tmp_pts = new List<Point3d>();
                List<double> multi_W = new List<double>(), multi_A = new List<double>(), multi_F = new List<double>();
                int j = 0;  // used for creating a new branch (GH_Path) for storing pts which is itself a list of points

                foreach (double m_val in multiple_m)
                {
                    width = Cal_W(length, m_val); // length * (2 * EllipticE(m_val) / EllipticK(m_val) - 1)

                    if (width < 0 & ignoreSelfIntersecting)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "One curve is self-intersecting. To enable these, set ignoreSelfIntersecting to False");
                        continue;
                    }

                    if (m_val >= Defined.M_SKETCHY)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Accuracy of the curve whose width = " + Math.Round(width, 4) + " is not guaranteed");

                    angle = Cal_A(m_val); // Math.Asin(2 * m_val - 1)
                    refPlane.Origin = refPlane.PointAt(width / (double)2, 0, 0);  // adjust the origin of the reference plane so that the curve is centered about the y-axis (start of the curve is at x = -width/2)

                    tmp_pts = FindBendForm(length, width, m_val, angle, refPlane);
                    multi_pts.AddRange(tmp_pts, new GH_Path(j));
                    multi_crv.Add(MakeCurve(tmp_pts, angle, refPlane));

                    multi_W.Add(width);
                    if (flip_A)
                        angle = -angle;
                    multi_A.Add(angle);

                    E = E * Math.Pow(10, 9);  // Young's modulus input E is in GPa, so we convert to Pa here (= N/m^2)
                    multi_F.Add(Math.Pow(EllipticK(m_val), 2) * E * I / Math.Pow(length, 2));  // from reference {4} pg. 79

                    j += 1;
                    refPlane.Origin = PtA;  // reset the reference plane origin to PtA for the next m_val
                }

                // assign the outputs
                DA.SetDataTree(0, multi_pts);
                DA.SetDataList(1, multi_crv);
                DA.SetData(2, length);
                DA.SetDataList(3, multi_W);
                if (flip_H)
                    height = -height;
                DA.SetData(4, height);
                DA.SetDataList(5, multi_A);
                DA.SetDataList(6, multi_F);
            }
            else
            {
                if (m >= Defined.M_SKETCHY)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Accuracy of the curve at these parameters is not guaranteed");

                if (width < 0 & ignoreSelfIntersecting)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Curve is self-intersecting. To enable these, set ignoreSelfIntersecting to False");
                    return;
                }

                DataTree<Point3d> multi_pts = new DataTree<Point3d>();
                List<Point3d> pts = FindBendForm(length, width, m, angle, refPlane);
                multi_pts.AddRange(pts, new GH_Path(0));
                DA.SetDataTree(0, multi_pts);

                List<Curve> multi_crv = new List<Curve>();
                multi_crv.Add(MakeCurve(pts, angle, refPlane));
                DA.SetDataList(1, multi_crv);
                DA.SetData(2, length);
                List<double> multi_W = new List<double>() { width };
                DA.SetDataList(3, multi_W);
                if (flip_H)
                    height = -height;
                DA.SetData(4, height);
                if (flip_A)
                    angle = -angle;
                List<double> multi_A = new List<double>() { angle };
                DA.SetDataList(5, multi_A);

                E = E * Math.Pow(10, 9);  // Young's modulus input E is in GPa, so we convert to Pa here (= N/m^2)
                double F = Math.Pow(EllipticK(m), 2) * E * I / Math.Pow(length, 2);  // from reference {4} pg. 79. Note: the critical buckling (that makes the rod/wire start to bend) can be found at height=0 (width=length)

                List<double> multi_F = new List<double>() { F };
                DA.SetDataList(6, multi_F);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Properties.Resources.Elastica;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("BC14726B-9058-4784-97FD-7A6AD462E21A"); }
        }



        // Solve for the m parameter from length and width (reference {1} equation (34), except b = width and K(k) and E(k) should be K(m) and E(m))
        private double SolveMFromLengthWidth(double L, double w)
        {
            if (w == 0)
                return Defined.M_ZERO_W;// for the boundary condition width = 0, bypass the function and return the known m value

            int n = 1; // Iteration counter (quit if >MAXIT)
            double lower = 0; // m must be within this range
            double upper = 1;
            double m = double.NaN;
            double cwl;

            while ((upper - lower) > Defined.MAXERR && (n) < Defined.MAXIT) // Repeat until range narrow enough or MAXIT
            {
                m = (upper + lower) / 2;
                cwl = 2 * EllipticE(m) / EllipticK(m) - 1;  // calculate w/L with the test value of m
                if (cwl < w / L)
                    upper = m;
                else
                    lower = m;
                n += 1;
            }
            return m;
        }
        // Solve for the m parameter from length and height (reference {1} equation (33), except K(k) should be K(m) and k = sqrt(m))
        // Note that it's actually possible to find 2 valid values for m (hence 2 width values) at certain height values
        private List<double> SolveMFromLengthHeight(double L, double h)
        {
            int n = 1; // Iteration counter (quit if >MAXIT)
            double lower = 0; // m must be within this range
            double upper = 1;
            bool twoWidths = h / L >= Defined.DOUBLE_W_HL_RATIO & h / L < Defined.MAX_HL_RATIO;  // check to see if h/L is within the range where 2 solutions for the width are possible
            double m = double.NaN;
            List<double> mult_m = new List<double>();
            double chl;

            if (twoWidths)
            {
                // find the first of two possible solutions for m with the following limits:
                lower = Defined.M_DOUBLE_W;  // see constants at bottom of script
                upper = Defined.M_MAXHEIGHT;  // see constants at bottom of script
                while ((upper - lower) > Defined.MAXERR && (n) < Defined.MAXIT) // Repeat until range narrow enough or MAXIT
                {
                    m = (upper + lower) / 2;
                    chl = Math.Sqrt(m) / EllipticK(m);  // calculate h/L with the test value of m
                    if (chl > h / L)
                        upper = m;
                    else
                        lower = m;
                    n += 1;
                }
                mult_m.Add(m);

                // then find the second of two possible solutions for m with the following limits:
                lower = Defined.M_MAXHEIGHT;  // see constants at bottom of script
                upper = 1;
                while ((upper - lower) > Defined.MAXERR && (n) < Defined.MAXIT) // Repeat until range narrow enough or MAXIT
                {
                    m = (upper + lower) / 2;
                    chl = Math.Sqrt(m) / EllipticK(m);  // calculate h/L with the test value of m
                    if (chl < h / L)
                        upper = m;
                    else
                        lower = m;
                    n += 1;
                }

                if (m <= Defined.M_MAX)
                    mult_m.Add(m);
            }
            else
            {
                // find the one possible solution for the m parameter
                upper = Defined.M_DOUBLE_W;  // limit the upper end of the search to the maximum value of m for which only one solution exists
                while ((upper - lower) > Defined.MAXERR && (n) < Defined.MAXIT) // Repeat until range narrow enough or MAXIT
                {
                    m = (upper + lower) / 2;
                    chl = Math.Sqrt(m) / EllipticK(m);  // calculate h/L with the test value of m
                    if (chl > h / L)
                        upper = m;
                    else
                        lower = m;
                    n += 1;
                }
                mult_m.Add(m);
            }

            return mult_m;
        }

        // Solve for the m parameter from width and height (derived from reference {1} equations (33) and (34) with same notes as above)
        private double SolveMFromWidthHeight(double w, double h)
        {
            int n = 1; // Iteration counter (quit if >MAXIT)
            double lower = 0; // m must be within this range
            double upper = 1;
            double m = double.NaN;
            double cwh;

            while ((upper - lower) > Defined.MAXERR && (n) < Defined.MAXIT) // Repeat until range narrow enough or MAXIT
            {
                m = (upper + lower) / 2;
                cwh = (2 * EllipticE(m) - EllipticK(m)) / Math.Sqrt(m);  // calculate w/h with the test value of m
                if (cwh < w / h)
                    upper = m;
                else
                    lower = m;
                n += 1;
            }

            return m;
        }


        // Return the Complete Elliptic integral of the 2nd kind
        // Abramowitz and Stegun p.591, formula 17.3.12
        // Code from http://www.codeproject.com/Articles/566614/Elliptic-integrals
        public double EllipticE(double m)
        {
            double sum, term, above, below;
            sum = 1;
            term = 1;
            above = 1;
            below = 2;

            for (int i = 1; i <= 100; i++)
            {
                term *= above / below;
                sum -= Math.Pow(m, i) * Math.Pow(term, 2) / above;
                above += 2;
                below += 2;
            }
            sum *= 0.5 * Math.PI;
            return sum;
        }

        // Return the Complete Elliptic integral of the 1st kind
        // Abramowitz and Stegun p.591, formula 17.3.11
        // Code from http://www.codeproject.com/Articles/566614/Elliptic-integrals
        public double EllipticK(double m)
        {
            double sum, term, above, below;
            sum = 1;
            term = 1;
            above = 1;
            below = 2;

            for (int i = 1; i <= 100; i++)
            {
                term *= above / below;
                sum += Math.Pow(m, i) * Math.Pow(term, 2);
                above += 2;
                below += 2;
            }
            sum *= 0.5 * Math.PI;
            return sum;
        }

        // Calculate length based on height and an m parameter, derived from reference {1} equation (33), except K(k) should be K(m) and k = sqrt(m)
        private double Cal_L(double h, double m)
        {
            return h * EllipticK(m) / Math.Sqrt(m);
        }

        // Calculate width based on length and an m parameter, derived from reference {1} equation (34), except b = width and K(k) and E(k) should be K(m) and E(m)
        private double Cal_W(double L, double m)
        {
            return L * (2 * EllipticE(m) / EllipticK(m) - 1);
        }
        // Calculate height based on length and an m parameter, from reference {1} equation (33), except K(k) should be K(m) and k = sqrt(m)
        private double Cal_H(double L, double m)
        {
            return L * Math.Sqrt(m) / EllipticK(m);
        }

        // Calculate the unique m parameter based on a start tangent angle, from reference {2}, just above equation (9a), that states k = Sin(angle / 2 + Pi / 4),
        // but as m = k^2 and due to this script's need for an angle rotated 90° versus the one in reference {1}, the following formula is the result
        // New note: verified by reference {4}, pg. 78 at the bottom
        private double Cal_M(double a)
        {
            return (1 - Math.Cos(a)) / 2;  // equal to Sin^2(a/2) too
        }
        // Calculate start tangent angle based on an m parameter, derived from above formula
        private double Cal_A(double m)
        {
            return Math.Acos(1 - 2 * m);
        }
        // This is the heart of this script, taking the found (or specified) length, width, and angle values along with the found m parameter to create
        // a list of points that approximate the shape or form of the elastica. It works by finding the x and y coordinates (which are reversed versus
        // the original equations (12a) and (12b) from reference {2} due to the 90° difference in orientation) based on the tangent angle along the curve.
        // See reference {2} for more details on how they derived it. Note that to simplify things, the algorithm only calculates the points for half of the
        // curve, then mirrors those points along the y-axis.
        private List<Point3d> FindBendForm(double L, double w, double m, double ang, Plane refPln)
        {
            L = L / 2;  // because the below algorithm is based on the formulas in reference {2} for only half of the curve
            w = w / 2;  // same

            if (ang == 0)
            {
                List<Point3d> @out = new List<Point3d>();
                @out.Add(refPln.PointAt(w, 0, 0));
                @out.Add(refPln.PointAt(-w, 0, 0));
                return @out;
            }

            double x;
            double y;
            List<Point3d> halfCurvePts = new List<Point3d>();
            List<Point3d> fullCurvePts = new List<Point3d>();
            List<Point3d> translatedPts = new List<Point3d>();

            ang -= Math.PI / 2;  // a hack to allow this algorithm to work, since the original curve in paper {2} was rotated 90°
            double angB = ang + (-Math.PI / 2 - ang) / Defined.CURVEDIVS;  // angB is the 'lowercase theta' which should be in formula {2}(12b) as the interval
                                                                           // start [a typo...see equation(3)]. It's necessary to start angB at ang + [interval] instead of just ang due to integration failing at angB = ang
            halfCurvePts.Add(new Point3d(w, 0, 0));  // start with this known initial point, as integration will fail when angB = ang

            // each point {x, y} is calculated from the tangent angle, angB, that occurs at each point (which is why this iterates from ~ang to -pi/2, the known end condition)
            while (Math.Round(angB, Defined.ROUNDTO) >= Math.Round(-Math.PI / 2, Defined.ROUNDTO))
            {
                y = (Math.Sqrt(2) * Math.Sqrt(Math.Sin(ang) - Math.Sin(angB)) * (w + L)) / (2 * EllipticE(m));  // note that x and y are swapped vs. (12a) and (12b)
                x = (L / (Math.Sqrt(2) * EllipticK(m))) * Simpson(angB, -Math.PI / 2, 500, ang);  // calculate the Simpson approximation of the integral (function f below)
                                                                                                  // over the interval angB ('lowercase theta') to -pi/2. side note: is 500 too few iterations for the Simson algorithm?

                if (Math.Round(x, Defined.ROUNDTO) == 0)
                    x = 0;
                halfCurvePts.Add(new Point3d(x, y, 0));

                angB += (-Math.PI / 2 - ang) / Defined.CURVEDIVS;  // onto the next tangent angle
            }

            // After finding the x and y values for half of the curve, add the {-x, y} values for the rest of the curve
            foreach (Point3d point in halfCurvePts)
            {
                if (Math.Round(point.X, Defined.ROUNDTO) == 0)
                {
                    if (Math.Round(point.Y, Defined.ROUNDTO) == 0)
                        fullCurvePts.Add(new Point3d(0, 0, 0));// special case when width = 0: when x = 0, only duplicate the point when y = 0 too
                }
                else
                    fullCurvePts.Add(new Point3d(-point.X, point.Y, 0));
            }
            halfCurvePts.Reverse();
            fullCurvePts.AddRange(halfCurvePts);

            foreach (Point3d p in fullCurvePts)
                translatedPts.Add(refPln.PointAt(p.X, p.Y, p.Z));// translate the points from the reference plane to the world plane

            return translatedPts;
        }

        // Interpolates the points from FindBendForm to create the Elastica curve. Uses start & end tangents for greater accuracy.
        private Curve MakeCurve(List<Point3d> pts, double ang, Plane refPln)
        {
            if (ang != 0)
            {
                Vector3d ts = new Vector3d(refPln.XAxis), te = new Vector3d(refPln.XAxis);
                ts.Rotate(ang, refPln.ZAxis);
                te.Rotate(-ang, refPln.ZAxis);
                return Curve.CreateInterpolatedCurve(pts, 3, CurveKnotStyle.Chord, ts, te);  // 3rd degree curve with 'Chord' Knot Style
            }
            else
                return Curve.CreateInterpolatedCurve(pts, 3);// if angle (and height) = 0, then simply interpolate the straight line (no start/end tangents)
        }

        // Implements the Simpson approximation for an integral of function f below
        public double Simpson(double a, double b, int n, double theta) // n should be an even number
        {
            int j;
            double s1;
            double s2;
            double h;
            h = (b - a) / n;
            s1 = 0;
            s2 = 0;
            for (j = 1; j <= n - 1; j += 2)
                s1 = s1 + fn(a + j * h, theta);
            for (j = 2; j <= n - 2; j += 2)
                s2 = s2 + fn(a + j * h, theta);
            return h / 3 * (fn(a, theta) + 4 * s1 + 2 * s2 + fn(b, theta));
        }

        // Specific calculation for the above integration
        public double fn(double x, double theta)
        {
            return Math.Sin(x) / (Math.Sqrt(Math.Sin(theta) - Math.Sin(x)));  // from reference {2} formula (12b)
        }


        internal sealed partial class Defined
        {
            private Defined()
            {
            }

            // Note: most of these values for m and h/L ratio were found with Wolfram Alpha and either specific intercepts (x=0) or local minima/maxima. They should be constant.
            public const double M_SKETCHY = 0.95;  // value of the m parameter where the curvature near the ends of the curve gets wonky
            public const double M_MAX = 0.993;  // maximum useful value of the m parameter, above which this algorithm for the form of the curve breaks down
            public const double M_ZERO_W = 0.826114765984970336;  // value of the m parameter when width = 0
            public const double M_MAXHEIGHT = 0.701327460663101223;  // value of the m parameter at maximum possible height of the bent rod/wire
            public const double M_DOUBLE_W = 0.180254422335013983;  // minimum value of the m parameter when two width values are possible for a given height and length
            public const double DOUBLE_W_HL_RATIO = 0.257342117984635757;  // value of the height/length ratio above which there are two possible width values
            public const double MAX_HL_RATIO = 0.403140189705650243;  // maximum possible value of the height/length ratio

            public const double MAXERR = 0.0000000001;  // error tolerance
            public const int MAXIT = 100;  // maximum number of iterations
            public const int ROUNDTO = 10;  // number of decimal places to round off to
            public const int CURVEDIVS = 50;  // number of sample points for building the curve (or half-curve as it were)
        }
    }
}
