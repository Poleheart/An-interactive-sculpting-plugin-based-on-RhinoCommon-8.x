using Rhino.Geometry;

namespace SubDSculptTest
{
    /// <summary>
    /// 笔刷类型
    /// </summary>
    public enum BrushType
    {
        Grab,       // 抓取: 跟随鼠标拖拽移动顶点
        Inflate,    // 膨胀: 沿法线或径向推挤顶点
        Smooth,     // 平滑: Laplacian 松弛顶点
        Deflate,    // 收缩: 沿法线或径向拉入顶点（膨胀的反向）
        Flatten,    // 展平: 将顶点移动到局部参考平面上
        Pinch,      // 夹捏: 将顶点向笔刷中心线收缩
        Twist,      // 扭转: 绕起笔轴旋转顶点
        Clay,       // 堆积: 沿法线推挤，平顶效果
        Crease      // 折痕: 向笔刷路径中心线挤压形成凹痕
    }

    /// <summary>
    /// 投影方向模式
    /// </summary>
    public enum ProjectionMode
    {
        View,       // 视图: 沿屏幕拖拽方向（过点且平行于视图平面的平面）
        Normal,     // 法线: 沿曲面法线方向
        XAxis,      // X轴: 沿世界X方向
        YAxis,      // Y轴: 沿世界Y方向
        ZAxis       // Z轴: 沿世界Z方向
    }

    /// <summary>
    /// 衰减曲线类型
    /// </summary>
    public enum FalloffType
    {
        Smooth,     // 平滑: 3t² - 2t³ (smoothstep)
        Linear,     // 线性: 1 - t
        Sharp,      // 锐利: (1 - t)²
        Root,       // 根号: sqrt(1 - t)
        Constant    // 无衰减: 1.0 (硬边)
    }

    /// <summary>
    /// 笔刷变形数学计算（纯函数，无状态）
    /// </summary>
    public static class BrushDeformer
    {
        /// <summary>
        /// 平滑阶梯函数: 0→1 平滑过渡
        /// </summary>
        public static double Smoothstep(double t)
        {
            t = System.Math.Max(0, System.Math.Min(1, t));
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// 衰减曲线求值
        /// </summary>
        /// <param name="type">衰减类型</param>
        /// <param name="t">归一化距离 [0,1]，0=中心，1=边缘</param>
        /// <returns>衰减权重 [0,1]</returns>
        public static double EvaluateFalloff(FalloffType type, double t)
        {
            t = System.Math.Max(0, System.Math.Min(1, t));
            switch (type)
            {
                case FalloffType.Smooth:   return 1.0 - (3 * t * t - 2 * t * t * t);
                case FalloffType.Linear:   return 1.0 - t;
                case FalloffType.Sharp:    return (1.0 - t) * (1.0 - t);
                case FalloffType.Root:     return System.Math.Sqrt(1.0 - t);
                case FalloffType.Constant: return 1.0;
                default:                   return 1.0 - (3 * t * t - 2 * t * t * t);
            }
        }

        /// <summary>
        /// 获取方向模式对应的世界方向向量
        /// </summary>
        public static Vector3d GetDirection(ProjectionMode proj, Vector3d surfaceNormal)
        {
            switch (proj)
            {
                case ProjectionMode.Normal: return surfaceNormal;
                case ProjectionMode.XAxis: return Vector3d.XAxis;
                case ProjectionMode.YAxis: return Vector3d.YAxis;
                case ProjectionMode.ZAxis: return Vector3d.ZAxis;
                default: return Vector3d.ZAxis; // View fallback
            }
        }

        /// <summary>
        /// 抓取: 将鼠标拖拽移动传递给顶点
        /// viewDisp3D: 视图模式下预计算的视图平面3D位移（由调用方计算）
        /// axisDisp: X/Y/Z轴模式下预计算的视图平面3D位移（用于提取轴分量）
        /// </summary>
        public static Point3d Grab(Point3d lockedBasePos, Point3d startPos, Point3d currentPos,
            double falloff, double strength, ProjectionMode proj, Vector3d surfaceNormal,
            Vector3d viewDisp3D, Vector3d axisDisp)
        {
            switch (proj)
            {
                case ProjectionMode.View:
                    return lockedBasePos + viewDisp3D * strength * falloff;

                case ProjectionMode.Normal:
                    var mouseDelta = currentPos - startPos;
                    double normalComp = mouseDelta * surfaceNormal;
                    return lockedBasePos + surfaceNormal * normalComp * strength * falloff;

                case ProjectionMode.XAxis:
                case ProjectionMode.YAxis:
                case ProjectionMode.ZAxis:
                    var dir = GetDirection(proj, surfaceNormal);
                    // 用视图平面位移投影到目标轴，避免CPlane约束问题
                    double component = axisDisp * dir;
                    return lockedBasePos + dir * component * strength * falloff;

                default:
                    return lockedBasePos;
            }
        }

        /// <summary>
        /// 膨胀: 沿方向推挤顶点
        /// </summary>
        public static Point3d Inflate(Point3d lockedBasePos, Point3d brushCenter,
            double falloff, double strength, double brushRadius,
            ProjectionMode proj, Vector3d surfaceNormal)
        {
            var dir = lockedBasePos - brushCenter;
            double dist = dir.Length;
            if (dist < 0.001) return lockedBasePos;

            double nd = dist / brushRadius;
            double distFalloff = 1.0 - Smoothstep(nd);

            switch (proj)
            {
                case ProjectionMode.View:
                    dir.Unitize();
                    return lockedBasePos + dir * strength * falloff * distFalloff;

                case ProjectionMode.Normal:
                case ProjectionMode.XAxis:
                case ProjectionMode.YAxis:
                case ProjectionMode.ZAxis:
                    var axis = GetDirection(proj, surfaceNormal);
                    return lockedBasePos + axis * strength * falloff * distFalloff;

                default:
                    return lockedBasePos;
            }
        }

        /// <summary>
        /// 平滑: 向笔刷中心松弛顶点（简单拉普拉斯近似）
        /// </summary>
        public static Point3d Smooth(Point3d lockedBasePos, Point3d brushCenter,
            double falloff, double strength)
        {
            var dir = brushCenter - lockedBasePos;
            return lockedBasePos + dir * strength * falloff * 0.3;
        }

        /// <summary>
        /// 收缩: 与膨胀相反，沿方向拉入顶点
        /// </summary>
        public static Point3d Deflate(Point3d lockedBasePos, Point3d brushCenter,
            double falloff, double strength, double brushRadius,
            ProjectionMode proj, Vector3d surfaceNormal)
        {
            var dir = lockedBasePos - brushCenter;
            double dist = dir.Length;
            if (dist < 0.001) return lockedBasePos;

            double nd = dist / brushRadius;
            double distFalloff = 1.0 - Smoothstep(nd);

            switch (proj)
            {
                case ProjectionMode.View:
                    dir.Unitize();
                    return lockedBasePos - dir * strength * falloff * distFalloff;

                case ProjectionMode.Normal:
                case ProjectionMode.XAxis:
                case ProjectionMode.YAxis:
                case ProjectionMode.ZAxis:
                    var axis = GetDirection(proj, surfaceNormal);
                    return lockedBasePos - axis * strength * falloff * distFalloff;

                default:
                    return lockedBasePos;
            }
        }

        /// <summary>
        /// 展平: 将顶点移动到以笔刷中心为原点的参考平面上
        /// </summary>
        public static Point3d Flatten(Point3d lockedBasePos, Point3d brushCenter,
            double falloff, double strength, ProjectionMode proj, Vector3d surfaceNormal)
        {
            // 参考平面: 过笔刷中心，法线为方向向量
            var axis = GetDirection(proj, surfaceNormal);
            var offset = lockedBasePos - brushCenter;
            double height = offset * axis;
            var projected = lockedBasePos - axis * height;
            return lockedBasePos + (projected - lockedBasePos) * strength * falloff;
        }

        /// <summary>
        /// 夹捏: 将顶点向笔刷中心收缩
        /// </summary>
        public static Point3d Pinch(Point3d lockedBasePos, Point3d brushCenter,
            double falloff, double strength, double brushRadius)
        {
            var dir = brushCenter - lockedBasePos;
            double dist = dir.Length;
            if (dist < 0.001) return lockedBasePos;

            dir.Unitize();
            double nd = dist / brushRadius;
            double distFalloff = 1.0 - Smoothstep(nd);
            return lockedBasePos + dir * strength * falloff * distFalloff * dist;
        }

        /// <summary>
        /// 扭转: 绕指定轴旋转顶点
        /// </summary>
        public static Point3d Twist(Point3d lockedBasePos, Point3d brushCenter,
            Point3d startPos, Point3d currentPos,
            double falloff, double strength, ProjectionMode proj, Vector3d surfaceNormal)
        {
            var toVertex = lockedBasePos - brushCenter;
            double dist = toVertex.Length;
            if (dist < 0.001) return lockedBasePos;

            var mouseDelta = currentPos - startPos;

            // 旋转轴: 根据方向模式选择
            Vector3d axis;
            if (proj == ProjectionMode.View)
            {
                // 视图模式: 用鼠标移动方向和法线叉积确定旋转轴
                axis = surfaceNormal;
            }
            else
            {
                axis = GetDirection(proj, surfaceNormal);
            }

            // 旋转角度: 鼠标移动距离
            double rotAngle = mouseDelta.Length * strength * falloff * 2.0;

            // 判断旋转方向
            var cross = Vector3d.CrossProduct(toVertex, mouseDelta);
            if (cross * axis < 0) rotAngle = -rotAngle;

            var xform = Transform.Rotation(rotAngle, axis, brushCenter);
            var result = lockedBasePos;
            result.Transform(xform);
            return result;
        }

        /// <summary>
        /// 堆积: 沿法线推挤，使用平坦衰减形成平顶效果
        /// </summary>
        public static Point3d Clay(Point3d lockedBasePos, Point3d brushCenter,
            double falloff, double strength, double brushRadius,
            ProjectionMode proj, Vector3d surfaceNormal)
        {
            var dir = lockedBasePos - brushCenter;
            double dist = dir.Length;
            if (dist < 0.001) return lockedBasePos;

            // 使用平坦衰减: 中间区域几乎等权，边缘快速衰减
            double nd = dist / brushRadius;
            double flatFalloff = nd < 0.6 ? 1.0 : 1.0 - Smoothstep((nd - 0.6) / 0.4);

            switch (proj)
            {
                case ProjectionMode.View:
                    dir.Unitize();
                    return lockedBasePos + dir * strength * falloff * flatFalloff;

                case ProjectionMode.Normal:
                case ProjectionMode.XAxis:
                case ProjectionMode.YAxis:
                case ProjectionMode.ZAxis:
                    var axis = GetDirection(proj, surfaceNormal);
                    return lockedBasePos + axis * strength * falloff * flatFalloff;

                default:
                    return lockedBasePos;
            }
        }

        /// <summary>
        /// 折痕: 将顶点向笔刷路径中心线挤压形成凹痕
        /// </summary>
        public static Point3d Crease(Point3d lockedBasePos, Point3d brushCenter,
            Point3d startPos, Point3d currentPos,
            double falloff, double strength, double brushRadius)
        {
            // 笔刷路径: 从 startPos 到 currentPos 的线段
            var pathDir = currentPos - startPos;
            double pathLen = pathDir.Length;
            if (pathLen < 0.001) return lockedBasePos;
            pathDir.Unitize();

            // 将顶点投影到路径线上，找到最近点
            var toVertex = lockedBasePos - startPos;
            double t = toVertex * pathDir;
            t = System.Math.Max(0, System.Math.Min(pathLen, t));
            var closestOnPath = startPos + pathDir * t;

            // 顶点到路径线的距离
            var toLine = lockedBasePos - closestOnPath;
            double dist = toLine.Length;
            if (dist < 0.001) return lockedBasePos;

            // 距离衰减
            double nd = dist / brushRadius;
            double distFalloff = 1.0 - Smoothstep(nd);

            // 向路径线收缩
            toLine.Unitize();
            return lockedBasePos - toLine * strength * falloff * distFalloff;
        }
    }
}
