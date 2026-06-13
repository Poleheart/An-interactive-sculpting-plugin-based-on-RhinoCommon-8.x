using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace SubDSculptTest
{
    [CommandStyle(Style.ScriptRunner)]
    public class TestSubDCreateCommand : Command
    {
        public override string EnglishName => "TestSubDCreate";

        /// <summary>
        /// 将 SubDVertexList (链表) 转为 List
        /// </summary>
        internal static List<SubDVertex> CollectVertices(SubD subd)
        {
            var list = new List<SubDVertex>();
            var v = subd.Vertices.First;
            while (v != null)
            {
                list.Add(v);
                v = v.Next;
            }
            return list;
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("\n===== SubD API 测试 1: 创建与拓扑读取 =====");

            // --- 1. 创建 SubD (从 Mesh 转换) ---
            RhinoApp.WriteLine("[1] 创建 SubD...");
            SubD subd = null;

            try
            {
                var sphere = new Sphere(Plane.WorldXY, 5.0);
                var mesh = Mesh.CreateFromSphere(sphere, 16, 16);
                if (mesh != null)
                {
                    RhinoApp.WriteLine($"  Mesh: {mesh.Vertices.Count} 顶点, {mesh.Faces.Count} 面");

                    // 最简方式: SubD.CreateFromMesh(mesh)
                    subd = SubD.CreateFromMesh(mesh);
                    if (subd != null)
                        RhinoApp.WriteLine("  ✓ SubD.CreateFromMesh(mesh) 成功");
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  ✗ 创建失败: {ex.Message}");
            }

            if (subd == null)
            {
                RhinoApp.WriteLine("  ✗ 无法创建 SubD");
                return Result.Failure;
            }

            // --- 2. 拓扑信息 ---
            RhinoApp.WriteLine($"\n[2] 拓扑信息:");
            RhinoApp.WriteLine($"  顶点数: {subd.Vertices.Count}");

            // 面/边需要遍历计数
            int faceCount = 0;
            foreach (var f in subd.Faces) faceCount++;
            int edgeCount = 0;
            foreach (var e in subd.Edges) edgeCount++;
            RhinoApp.WriteLine($"  面数:   {faceCount}");
            RhinoApp.WriteLine($"  边数:   {edgeCount}");

            // --- 3. 遍历顶点 (链表方式) ---
            var vertices = CollectVertices(subd);
            RhinoApp.WriteLine($"\n[3] 前 5 个顶点:");
            for (int i = 0; i < Math.Min(5, vertices.Count); i++)
            {
                var pt = vertices[i].ControlNetPoint;
                RhinoApp.WriteLine($"  V[{i}]: ({pt.X:F4}, {pt.Y:F4}, {pt.Z:F4})");
            }

            // --- 4. 添加到文档 ---
            RhinoApp.WriteLine($"\n[4] 添加到文档...");
            try
            {
                Guid id = doc.Objects.AddSubD(subd);
                RhinoApp.WriteLine(id != Guid.Empty
                    ? $"  ✓ 已添加 ({id})"
                    : "  ✗ 返回空 GUID");
                doc.Views.Redraw();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  AddSubD 失败: {ex.Message}");
                try { doc.Objects.Add(subd); doc.Views.Redraw(); RhinoApp.WriteLine("  ✓ Objects.Add 成功"); }
                catch (Exception ex2) { RhinoApp.WriteLine($"  ✗ {ex2.Message}"); }
            }

            RhinoApp.WriteLine("\n===== 测试 1 完成 =====");
            return Result.Success;
        }
    }
}
