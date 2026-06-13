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
    public class TestBrushSimCommand : Command
    {
        public override string EnglishName => "TestBrushSim";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("\n===== SubD API 测试 3: 笔刷模拟 =====");

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

            var vertices = TestSubDCreateCommand.CollectVertices(subd);
            RhinoApp.WriteLine($"  ✓ 找到 SubD ({vertices.Count} 顶点)");

            // 顶点缓存 + RTree
            RhinoApp.WriteLine("\n[1] 构建顶点缓存...");
            var vertexPositions = new Point3d[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
                vertexPositions[i] = vertices[i].ControlNetPoint;

            var rtree = new RTree();
            for (int i = 0; i < vertexPositions.Length; i++)
                rtree.Insert(vertexPositions[i], i);
            RhinoApp.WriteLine($"  ✓ RTree 构建完成");

            // 三角化
            RhinoApp.WriteLine("[2] 构建三角化网格...");
            Mesh mesh = null;
            try
            {
                mesh = Mesh.CreateFromSubD(subd, 3);
                RhinoApp.WriteLine(mesh != null
                    ? $"  ✓ Mesh: {mesh.Vertices.Count} 顶点, {mesh.Faces.Count} 面"
                    : "  ⚠ 返回 null");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  ⚠ {ex.Message}");
            }

            // 空间查询性能
            RhinoApp.WriteLine("\n[3] 空间查询性能测试...");
            var sw = Stopwatch.StartNew();
            int queryCount = 1000, totalFound = 0;
            var rand = new Random(42);
            for (int q = 0; q < queryCount; q++)
            {
                var c = new Point3d(rand.NextDouble() * 10, rand.NextDouble() * 10, rand.NextDouble() * 10);
                int found = 0;
                rtree.Search(new Sphere(c, 2.0), (s, a) => { found++; });
                totalFound += found;
            }
            sw.Stop();
            RhinoApp.WriteLine($"  ✓ {queryCount} 次查询, 找到 {totalFound}, 耗时 {sw.ElapsedMilliseconds} ms");

            // 衰减计算性能
            RhinoApp.WriteLine("\n[4] 衰减计算性能...");
            sw.Restart();
            int evalCount = 100000;
            double sum = 0;
            for (int i = 0; i < evalCount; i++)
            {
                double t = i / (double)evalCount;
                sum += 1.0 - (3 * t * t - 2 * t * t * t);
            }
            sw.Stop();
            RhinoApp.WriteLine($"  ✓ {evalCount} 次, 耗时 {sw.ElapsedMilliseconds} ms");

            // 模拟 Inflate 笔刷
            RhinoApp.WriteLine("\n[5] 模拟 Inflate 笔刷...");
            var brushCenter = vertexPositions[0];
            double brushRadius = 2.0, brushStrength = 0.5;

            var influenced = new List<(int idx, double weight)>();
            rtree.Search(new Sphere(brushCenter, brushRadius), (s, a) =>
            {
                double dist = vertexPositions[a.Id].DistanceTo(brushCenter);
                double nd = dist / brushRadius;
                double falloff = 1.0 - (3 * nd * nd - 2 * nd * nd * nd);
                influenced.Add((a.Id, falloff));
            });
            RhinoApp.WriteLine($"  影响顶点数: {influenced.Count}");

            sw.Restart();
            foreach (var (idx, weight) in influenced)
            {
                var pos = vertexPositions[idx];
                var dir = pos - brushCenter;
                if (dir.Length > 0.001)
                {
                    dir.Unitize();
                    vertices[idx].SetControlNetPoint(pos + dir * brushStrength * weight, false);
                }
            }
            doc.Objects.Replace(rhObj.Id, subd);
            doc.Views.Redraw();
            sw.Stop();
            RhinoApp.WriteLine($"  ✓ 涂抹完成, 耗时 {sw.ElapsedMilliseconds} ms");

            // API 汇总
            RhinoApp.WriteLine("\n===== API 兼容性汇总 =====");
            RhinoApp.WriteLine($"  SubD.CreateFromMesh    : ✓");
            RhinoApp.WriteLine($"  Vertices.First + .Next : ✓ (链表遍历)");
            RhinoApp.WriteLine($"  Vertex.ControlNetPoint : ✓");
            RhinoApp.WriteLine($"  SetControlNetPoint(pt, false) : ✓");
            RhinoApp.WriteLine($"  Objects.Replace(id, subd)     : ✓");
            RhinoApp.WriteLine($"  RTree.Search(Sphere)   : ✓");
            RhinoApp.WriteLine($"  Mesh.CreateFromSubD    : {(mesh != null ? "✓" : "⚠")}");
            RhinoApp.WriteLine("===============================");

            // 恢复
            RhinoApp.WriteLine("\n[i] 恢复...");
            for (int i = 0; i < vertices.Count; i++)
                vertices[i].SetControlNetPoint(vertexPositions[i], false);
            doc.Objects.Replace(rhObj.Id, subd);
            doc.Views.Redraw();
            RhinoApp.WriteLine("  ✓ 已恢复");

            return Result.Success;
        }
    }
}
