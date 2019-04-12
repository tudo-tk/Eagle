using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Eagle
{
    public class AntFarmGrasshopperInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "Eagle";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return Properties.Resources.Eagle;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "Grasshopper Toolbar - TU Dortmund";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("c7e067a2-d35c-41c5-80e3-51e2adb90494");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "TK";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "tragkonstruktionen.bauwesen@tu-dortmund.de";
            }
        }
    }
}
