using System;
using Rhino;
using Rhino.PlugIns;

namespace SubDSculptTest
{
    [System.Runtime.InteropServices.Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public class SubDSculptTestPlugin : PlugIn
    {
        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            RhinoApp.WriteLine("=== SubD 雕刻插件已加载 ===");
            RhinoApp.WriteLine("命令: SculptBrush — SubD 雕刻笔刷");
            RhinoApp.WriteLine("命令: MeshSculptBrush — 网格雕刻笔刷");
            RhinoApp.WriteLine("命令: NurbsSculptBrush — NURBS 曲面雕刻笔刷");
            return LoadReturnCode.Success;
        }
    }
}
