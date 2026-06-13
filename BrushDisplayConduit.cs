using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace SubDSculptTest
{
    /// <summary>
    /// 笔刷显示管道: 笔刷光标、受影响顶点、变形预览、蒙版覆盖
    /// </summary>
    public class BrushDisplayConduit : DisplayConduit
    {
        public Point3d HitPoint;
        public double BrushRadius = 2.0;
        public bool IsActive;
        public bool IsDragging;

        public Point3d[] AffectedPoints;
        public double[] AffectedWeights;
        public int AffectedCount;
        public Point3d[] DeformedPoints;
        public int DeformedCount;

        // 蒙版可视化
        public MaskData MaskData;
        public List<SubDVertex> Vertices; // SubD 用
        public Point3d[] VertexPositions; // Mesh/NURBS 用

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            var display = e.Display;

            // 蒙版覆盖: 始终显示（即使笔刷未激活）
            if (MaskData != null && MaskData.HasMask())
            {
                if (Vertices != null)
                    MaskData.DrawOverlay(display, Vertices);
                else if (VertexPositions != null)
                    MaskData.DrawOverlay(display, VertexPositions);
            }

            if (!IsActive) return;

            // 1. 笔刷球体: 半透明 + 轮廓
            var sphere = new Sphere(HitPoint, BrushRadius);
            var circle = new Circle(HitPoint, BrushRadius);
            display.DrawSphere(sphere, Color.FromArgb(30, 120, 200, 255), 3);
            display.DrawCircle(circle, Color.White, 2);

            // 2. 十字准星
            double cs = BrushRadius * 0.12;
            display.DrawLine(
                new Point3d(HitPoint.X - cs, HitPoint.Y, HitPoint.Z),
                new Point3d(HitPoint.X + cs, HitPoint.Y, HitPoint.Z), Color.White, 1);
            display.DrawLine(
                new Point3d(HitPoint.X, HitPoint.Y - cs, HitPoint.Z),
                new Point3d(HitPoint.X, HitPoint.Y + cs, HitPoint.Z), Color.White, 1);

            // 3. 受影响顶点: 红→绿 颜色编码
            if (AffectedPoints != null && AffectedWeights != null)
            {
                int count = System.Math.Min(AffectedCount, AffectedPoints.Length);
                for (int i = 0; i < count; i++)
                {
                    double w = System.Math.Max(0, System.Math.Min(1, AffectedWeights[i]));
                    int r = (int)(255 * (1 - w));
                    int g = (int)(255 * w);
                    display.DrawPoint(AffectedPoints[i], PointStyle.Simple, 3, Color.FromArgb(r, g, 0));
                }
            }

            // 4. 变形预览: 绿色点 + 位移线
            if (IsDragging && DeformedPoints != null)
            {
                int count = System.Math.Min(DeformedCount, DeformedPoints.Length);
                for (int i = 0; i < count; i++)
                {
                    display.DrawPoint(DeformedPoints[i], PointStyle.Simple, 4, Color.Lime);
                    if (AffectedPoints != null && i < AffectedCount)
                        display.DrawLine(AffectedPoints[i], DeformedPoints[i], Color.FromArgb(0, 180, 0), 1);
                }
            }

        }
    }
}
