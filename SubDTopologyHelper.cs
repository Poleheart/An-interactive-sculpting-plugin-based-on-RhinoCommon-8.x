using System.Collections.Generic;
using Rhino.Geometry;

namespace SubDSculptTest
{
    /// <summary>
    /// SubD 拓扑辅助: 邻域关系、Laplacian
    /// </summary>
    public static class SubDTopologyHelper
    {
        /// <summary>
        /// 构建顶点邻域索引表（通过边遍历）
        /// </summary>
        public static Dictionary<int, List<int>> BuildAdjacency(List<SubDVertex> vertices, SubD subd)
        {
            var adj = new Dictionary<int, List<int>>();
            for (int i = 0; i < vertices.Count; i++)
                adj[i] = new List<int>();

            if (vertices.Count == 0 || subd == null) return adj;

            // 建立顶点对象引用→索引的映射
            var refToIdx = new Dictionary<SubDVertex, int>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
                refToIdx[vertices[i]] = i;

            // 遍历所有边建立邻接关系
            foreach (var edge in subd.Edges)
            {
                var vFrom = edge.VertexFrom;
                var vTo = edge.VertexTo;
                if (vFrom != null && vTo != null)
                {
                    int iFrom, iTo;
                    if (refToIdx.TryGetValue(vFrom, out iFrom) &&
                        refToIdx.TryGetValue(vTo, out iTo))
                    {
                        if (!adj[iFrom].Contains(iTo)) adj[iFrom].Add(iTo);
                        if (!adj[iTo].Contains(iFrom)) adj[iTo].Add(iFrom);
                    }
                }
            }

            return adj;
        }

        /// <summary>
        /// 构建 Mesh 顶点邻域索引表
        /// </summary>
        public static Dictionary<int, List<int>> BuildMeshAdjacency(Mesh mesh)
        {
            var adj = new Dictionary<int, List<int>>();
            int vCount = mesh.Vertices.Count;
            for (int i = 0; i < vCount; i++)
            {
                var connected = mesh.Vertices.GetConnectedVertices(i);
                var neighbors = new List<int>();
                if (connected != null)
                {
                    for (int j = 0; j < connected.Length; j++)
                        neighbors.Add(connected[j]);
                }
                adj[i] = neighbors;
            }
            return adj;
        }

        /// <summary>
        /// 计算 Laplacian 重心（邻域平均位置）
        /// </summary>
        public static Point3d LaplacianCenter(Point3d[] positions, List<int> neighbors)
        {
            if (neighbors == null || neighbors.Count == 0)
                return Point3d.Origin;

            var sum = Point3d.Origin;
            for (int i = 0; i < neighbors.Count; i++)
                sum += positions[neighbors[i]];
            return sum / neighbors.Count;
        }
    }
}
