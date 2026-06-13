using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace SubDSculptTest
{
    [CommandStyle(Style.ScriptRunner)]
    public class TestModifyVertexCommand : Command
    {
        public override string EnglishName => "TestModifyVertex";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("\n===== SubD API 测试 2: 顶点修改 =====");

            // 获取第一个 SubD
            var filter = new ObjectEnumeratorSettings { ObjectTypeFilter = ObjectType.SubD };
            SubD subd = null;
            RhinoObject rhObj = null;
            foreach (var obj in doc.Objects.GetObjectList(filter))
            {
                subd = obj.Geometry as SubD;
                if (subd != null) { rhObj = obj; break; }
            }

            if (subd == null || rhObj == null)
            {
                RhinoApp.WriteLine("  ✗ 文档中没有 SubD，请先运行 TestSubDCreate");
                return Result.Failure;
            }

            // 链表遍历收集顶点
            var vertices = TestSubDCreateCommand.CollectVertices(subd);
            RhinoApp.WriteLine($"  ✓ 找到 SubD ({vertices.Count} 顶点)");

            // 记录原始位置
            var originalPositions = new List<Point3d>();
            for (int i = 0; i < vertices.Count; i++)
                originalPositions.Add(vertices[i].ControlNetPoint);

            for (int i = 0; i < Math.Min(5, vertices.Count); i++)
            {
                var pt = originalPositions[i];
                RhinoApp.WriteLine($"  V[{i}]: ({pt.X:F4}, {pt.Y:F4}, {pt.Z:F4})");
            }

            // 测试 SetControlNetPoint
            RhinoApp.WriteLine($"\n[2] 测试 SetControlNetPoint...");
            bool canModify = false;
            try
            {
                var v0 = vertices[0];
                var originalPt = v0.ControlNetPoint;
                var newPt = new Point3d(originalPt.X, originalPt.Y, originalPt.Z + 0.5);
                v0.SetControlNetPoint(newPt, false);
                canModify = true;

                var verifyPt = v0.ControlNetPoint;
                RhinoApp.WriteLine($"  ✓ SetControlNetPoint 成功");
                RhinoApp.WriteLine($"    修改前: ({originalPt.X:F4}, {originalPt.Y:F4}, {originalPt.Z:F4})");
                RhinoApp.WriteLine($"    修改后: ({verifyPt.X:F4}, {verifyPt.Y:F4}, {verifyPt.Z:F4})");

                // 恢复
                v0.SetControlNetPoint(originalPt, false);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  ✗ SetControlNetPoint 失败: {ex.Message}");
            }

            if (canModify)
            {
                // 批量修改
                RhinoApp.WriteLine($"\n[3] 批量修改性能测试...");
                var sw = Stopwatch.StartNew();
                int modifiedCount = 0;
                var center = new Point3d(0, 0, 0);
                double radius = 2.0;
                double strength = 0.3;

                for (int i = 0; i < vertices.Count; i++)
                {
                    var pos = vertices[i].ControlNetPoint;
                    double dist = pos.DistanceTo(center);
                    if (dist < radius)
                    {
                        var dir = pos - center;
                        if (dir.Length > 0.001)
                        {
                            dir.Unitize();
                            double falloff = 1.0 - (dist / radius);
                            vertices[i].SetControlNetPoint(pos + dir * strength * falloff, false);
                            modifiedCount++;
                        }
                    }
                }
                sw.Stop();
                RhinoApp.WriteLine($"  ✓ 修改 {modifiedCount} 个顶点, 耗时 {sw.ElapsedMilliseconds} ms");
                if (modifiedCount > 0)
                    RhinoApp.WriteLine($"    单顶点: {(sw.Elapsed.TotalMilliseconds / modifiedCount * 1000):F2} μs");

                // 更新文档
                RhinoApp.WriteLine($"\n[4] 更新文档...");
                doc.Objects.Replace(rhObj.Id, subd);
                doc.Views.Redraw();
                RhinoApp.WriteLine("  ✓ 已更新");

                // 恢复
                RhinoApp.WriteLine($"\n[5] 恢复原始形状...");
                for (int i = 0; i < vertices.Count; i++)
                    vertices[i].SetControlNetPoint(originalPositions[i], false);
                doc.Objects.Replace(rhObj.Id, subd);
                doc.Views.Redraw();
                RhinoApp.WriteLine("  ✓ 已恢复");
            }

            RhinoApp.WriteLine("\n===== 测试 2 完成 =====");
            return Result.Success;
        }
    }
}
