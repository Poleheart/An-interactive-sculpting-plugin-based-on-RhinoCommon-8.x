using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;

namespace SubDSculptTest
{
    /// <summary>
    /// 蒙版数据: 每个顶点一个 mask 值 [0,1]
    /// 0 = 完全保护, 1 = 完全暴露
    /// </summary>
    public class MaskData
    {
        private double[] _values;
        private int _count;

        public int Count => _count;

        public MaskData(int vertexCount)
        {
            _count = vertexCount;
            _values = new double[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                _values[i] = 1.0; // 默认全部暴露
        }

        public double GetValue(int index)
        {
            if (index >= 0 && index < _count) return _values[index];
            return 1.0;
        }

        public void SetValue(int index, double value)
        {
            if (index >= 0 && index < _count)
                _values[index] = System.Math.Max(0, System.Math.Min(1, value));
        }

        /// <summary>
        /// 绘制蒙版: 降低 mask 值（保护区域，不受笔刷影响）
        /// </summary>
        public void Paint(int[] indices, double[] falloffWeights, int count, double strength)
        {
            for (int i = 0; i < count; i++)
            {
                int idx = indices[i];
                double old = GetValue(idx);
                double sub = falloffWeights[i] * strength;
                SetValue(idx, System.Math.Max(0.0, old - sub));
            }
        }

        /// <summary>
        /// 擦除蒙版: 增加 mask 值（暴露区域，恢复笔刷影响）
        /// </summary>
        public void Erase(int[] indices, double[] falloffWeights, int count, double strength)
        {
            for (int i = 0; i < count; i++)
            {
                int idx = indices[i];
                double old = GetValue(idx);
                double add = falloffWeights[i] * strength;
                SetValue(idx, System.Math.Min(1.0, old + add));
            }
        }

        /// <summary>
        /// 模糊蒙版: 向邻域平均值靠拢
        /// </summary>
        public void Blur(Dictionary<int, List<int>> adjacency, int iterations = 1)
        {
            var temp = new double[_count];
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < _count; i++)
                    temp[i] = _values[i];

                for (int i = 0; i < _count; i++)
                {
                    if (!adjacency.ContainsKey(i) || adjacency[i].Count == 0) continue;
                    var neighbors = adjacency[i];
                    double sum = temp[i]; // 包含自身
                    for (int ni = 0; ni < neighbors.Count; ni++)
                        sum += temp[neighbors[ni]];
                    _values[i] = sum / (neighbors.Count + 1);
                }
            }
        }

        /// <summary>
        /// 锐化蒙版: 二值化
        /// </summary>
        public void Sharpen()
        {
            for (int i = 0; i < _count; i++)
                _values[i] = _values[i] > 0.5 ? 1.0 : 0.0;
        }

        /// <summary>
        /// 反转蒙版
        /// </summary>
        public void Invert()
        {
            for (int i = 0; i < _count; i++)
                _values[i] = 1.0 - _values[i];
        }

        /// <summary>
        /// 清除蒙版（全部暴露）
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _count; i++)
                _values[i] = 1.0;
        }

        /// <summary>
        /// 填充蒙版（全部保护）
        /// </summary>
        public void Fill()
        {
            for (int i = 0; i < _count; i++)
                _values[i] = 0.0;
        }

        /// <summary>
        /// 是否有任何非默认蒙版
        /// </summary>
        public bool HasMask()
        {
            for (int i = 0; i < _count; i++)
                if (_values[i] < 0.999) return true;
            return false;
        }

        /// <summary>
        /// 绘制蒙版覆盖层（SubD 用）
        /// </summary>
        public void DrawOverlay(DisplayPipeline display, List<SubDVertex> vertices)
        {
            for (int i = 0; i < _count && i < vertices.Count; i++)
            {
                if (_values[i] < 0.999)
                {
                    int alpha = (int)(220 * (1.0 - _values[i]));
                    var color = Color.FromArgb(System.Math.Max(30, alpha), 255, 40, 40);
                    display.DrawPoint(vertices[i].ControlNetPoint, PointStyle.Simple, 5, color);
                }
            }
        }

        /// <summary>
        /// 绘制蒙版覆盖层（Mesh 用，Point3d 数组）
        /// </summary>
        public void DrawOverlay(DisplayPipeline display, Point3d[] positions)
        {
            for (int i = 0; i < _count && i < positions.Length; i++)
            {
                if (_values[i] < 0.999)
                {
                    int alpha = (int)(220 * (1.0 - _values[i]));
                    var color = Color.FromArgb(System.Math.Max(30, alpha), 255, 40, 40);
                    display.DrawPoint(positions[i], PointStyle.Simple, 5, color);
                }
            }
        }
    }
}
