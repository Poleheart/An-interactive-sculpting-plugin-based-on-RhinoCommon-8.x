using System;
using System.Collections.Generic;
using System.Globalization;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace SubDSculptTest
{
    [CommandStyle(Style.ScriptRunner)]
    public class MeshSculptBrushCommand : Command
    {
        public override string EnglishName => "MeshSculptBrush";

        private double _brushRadius = 2.0;
        private double _brushStrength = 0.5;
        private double _maxDisplacement = 0.0;
        private BrushType _brushType = BrushType.Grab;
        private ProjectionMode _projMode = ProjectionMode.View;
        private FalloffType _falloffType = FalloffType.Smooth;
        private bool _maskEnabled = false;
        private bool _maskEraseMode = false;

        // 多对象支持
        private class SculptTarget
        {
            public Guid Id;
            public Mesh Mesh;
            public Mesh OriginalMesh;
            public Point3d[] BasePositions;
            public int VertexOffset;
        }
        private List<SculptTarget> _targets = new List<SculptTarget>();
        private int _totalVertexCount;

        private Point3d[] _allBasePositions;
        private Point3d[] _deformedPoints;
        private RTree _rtree;
        private BrushDisplayConduit _conduit;

        private int[] _lockedIndices;
        private double[] _lockedWeights;
        private Point3d[] _lockedBasePos;
        private int[] _lockedTargetIdx;
        private int _lockedCount;
        private int _bufferSize;

        private Point3d _strokeStartPoint;
        private Vector3d _surfaceNormal;

        private Dictionary<int, List<int>> _adjacency;
        private MaskData _maskData;

        // === 语言 ===
        private static bool _isChinese;
        private static bool _langDetected;

        private static bool IsChinese()
        {
            if (!_langDetected)
            {
                _langDetected = true;
                try
                {
                    int langId = Rhino.ApplicationSettings.AppearanceSettings.LanguageIdentifier;
                    _isChinese = langId == 2052 || langId == 1028;
                }
                catch
                {
                    _isChinese = CultureInfo.CurrentUICulture.Name.StartsWith("zh");
                }
            }
            return _isChinese;
        }

        private static string T(string zh, string en) => IsChinese() ? zh : en;

        private const string OptBrush = "笔刷";
        private const string OptRadius = "半径";
        private const string OptStrength = "强度";
        private const string OptDirection = "方向";
        private const string OptMaxDisp = "最大位移";
        private const string OptFalloff = "衰减";
        private const string OptMaskMode = "蒙版模式";
        private const string OptMaskToggle = "蒙版";
        private const string OptMaskClear = "清除蒙版";
        private const string OptMaskFill = "填充蒙版";
        private const string OptMaskInvert = "反转蒙版";

        private static string NameGrab => T("抓取", "Grab");
        private static string NameInflate => T("膨胀", "Inflate");
        private static string NameSmooth => T("平滑", "Smooth");
        private static string NameDeflate => T("收缩", "Deflate");
        private static string NameFlatten => T("展平", "Flatten");
        private static string NamePinch => T("夹捏", "Pinch");
        private static string NameTwist => T("扭转", "Twist");
        private static string NameClay => T("堆积", "Clay");
        private static string NameCrease => T("折痕", "Crease");

        private static string NameMaskOn => T("开启", "On");
        private static string NameMaskOff => T("关闭", "Off");
        private static string NameMaskPaint => T("绘制", "Paint");
        private static string NameMaskErase => T("擦除", "Erase");

        private static string NameView => T("视图", "View");
        private static string NameNormal => T("法线", "Normal");
        private static string NameX => T("X轴", "X");
        private static string NameY => T("Y轴", "Y");
        private static string NameZ => T("Z轴", "Z");

        private static string NameSmoothFalloff => T("平滑", "Smooth");
        private static string NameLinearFalloff => T("线性", "Linear");
        private static string NameSharpFalloff => T("锐利", "Sharp");
        private static string NameRootFalloff => T("根号", "Root");
        private static string NameConstantFalloff => T("硬边", "Constant");

        private string CurrentBrushName
        {
            get
            {
                switch (_brushType)
                {
                    case BrushType.Grab:     return NameGrab;
                    case BrushType.Inflate:  return NameInflate;
                    case BrushType.Smooth:   return NameSmooth;
                    case BrushType.Deflate:  return NameDeflate;
                    case BrushType.Flatten:  return NameFlatten;
                    case BrushType.Pinch:    return NamePinch;
                    case BrushType.Twist:    return NameTwist;
                    case BrushType.Clay:     return NameClay;
                    case BrushType.Crease:   return NameCrease;
                    default:                 return _brushType.ToString();
                }
            }
        }

        private string CurrentDirName
        {
            get
            {
                switch (_projMode)
                {
                    case ProjectionMode.View:   return NameView;
                    case ProjectionMode.Normal: return NameNormal;
                    case ProjectionMode.XAxis:  return NameX;
                    case ProjectionMode.YAxis:  return NameY;
                    case ProjectionMode.ZAxis:  return NameZ;
                    default:                    return _projMode.ToString();
                }
            }
        }

        private string CurrentFalloffName
        {
            get
            {
                switch (_falloffType)
                {
                    case FalloffType.Smooth:   return NameSmoothFalloff;
                    case FalloffType.Linear:   return NameLinearFalloff;
                    case FalloffType.Sharp:    return NameSharpFalloff;
                    case FalloffType.Root:     return NameRootFalloff;
                    case FalloffType.Constant: return NameConstantFalloff;
                    default:                   return _falloffType.ToString();
                }
            }
        }

        private void EnsureBufferSize(int required)
        {
            if (_bufferSize >= required) return;
            int newSize = Math.Max(required, 256);
            _lockedIndices = new int[newSize];
            _lockedWeights = new double[newSize];
            _lockedBasePos = new Point3d[newSize];
            _lockedTargetIdx = new int[newSize];
            _deformedPoints = new Point3d[newSize];
            _bufferSize = newSize;
            if (_conduit != null)
            {
                _conduit.AffectedPoints = new Point3d[newSize];
                _conduit.AffectedWeights = new double[newSize];
                _conduit.DeformedPoints = _deformedPoints;
            }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var brushNames = new string[] { NameGrab, NameInflate, NameSmooth, NameDeflate, NameFlatten, NamePinch, NameTwist, NameClay, NameCrease };
            var dirNames = new string[] { NameView, NameNormal, NameX, NameY, NameZ };
            var falloffNames = new string[] { NameSmoothFalloff, NameLinearFalloff, NameSharpFalloff, NameRootFalloff, NameConstantFalloff };

            // === 选择 Mesh 对象（支持多选）===
            var go0 = new GetObject();
            go0.SetCommandPrompt(T("选择要雕刻的网格（可多选）", "Select Mesh to sculpt (multi-select ok)"));
            go0.GeometryFilter = ObjectType.Mesh;
            go0.SubObjectSelect = false;
            go0.EnablePreSelect(true, true);
            if (go0.GetMultiple(1, 0) != GetResult.Object)
                return go0.CommandResult();

            _targets.Clear();
            _totalVertexCount = 0;
            for (int oi = 0; oi < go0.ObjectCount; oi++)
            {
                var rhObj = go0.Object(oi).Object();
                var mesh = rhObj.Geometry as Mesh;
                if (mesh == null) continue;

                var target = new SculptTarget
                {
                    Id = rhObj.Id,
                    Mesh = mesh,
                    OriginalMesh = mesh.Duplicate() as Mesh,
                    VertexOffset = _totalVertexCount
                };
                int vc = mesh.Vertices.Count;
                target.BasePositions = new Point3d[vc];
                for (int i = 0; i < vc; i++)
                    target.BasePositions[i] = mesh.Vertices.Point3dAt(i);

                _totalVertexCount += vc;
                _targets.Add(target);
            }

            if (_targets.Count == 0)
            {
                RhinoApp.WriteLine(T("未选中网格", "No Mesh selected"));
                return Result.Failure;
            }

            // 全局顶点数组
            _allBasePositions = new Point3d[_totalVertexCount];
            foreach (var t in _targets)
                Array.Copy(t.BasePositions, 0, _allBasePositions, t.VertexOffset, t.BasePositions.Length);

            // 拓扑
            _adjacency = new Dictionary<int, List<int>>();
            foreach (var t in _targets)
            {
                var adj = SubDTopologyHelper.BuildMeshAdjacency(t.Mesh);
                foreach (var kv in adj)
                    _adjacency[kv.Key + t.VertexOffset] = kv.Value.ConvertAll(v => v + t.VertexOffset);
            }

            RebuildRTree();

            _bufferSize = 0;
            _lockedIndices = null;
            _lockedWeights = null;
            _lockedBasePos = null;
            _lockedTargetIdx = null;
            _deformedPoints = null;

            _maskData = new MaskData(_totalVertexCount);

            // 构建全局 Mesh 顶点列表用于蒙版显示
            var allMeshPositions = new List<SubDVertex>(); // 复用 conduit 的 Vertices 字段
            // conduit 需要 List<SubDVertex> 但 Mesh 没有，用 null 跳过
            _conduit = new BrushDisplayConduit
            {
                BrushRadius = _brushRadius,
                MaskData = _maskData,
                Vertices = null,
                VertexPositions = _allBasePositions
            };
            _conduit.Enabled = true;

            doc.Views.Redraw();

            // === 第一级: 蒙版开关 + 绘制蒙版 ===
            const string OptMaskPaintEnter = "绘制蒙版";
            while (true)
            {
                var go = new GetOption();
                string maskInfo = _maskEnabled ? T(" | 蒙版:ON", " | Mask:ON") : "";
                go.SetCommandPrompt(T(
                    $"已选 {_targets.Count} 个网格，共 {_totalVertexCount} 顶点{maskInfo}。按 Enter 开始雕刻",
                    $"Selected {_targets.Count} Mesh, {_totalVertexCount} vertices{maskInfo}. Press Enter to start"));
                go.AcceptNothing(true);

                var maskToggleNames = new string[] { NameMaskOn, NameMaskOff };
                go.AddOptionList(OptMaskToggle, maskToggleNames, _maskEnabled ? 0 : 1);
                if (_maskEnabled)
                    go.AddOption(OptMaskPaintEnter);

                var result = go.Get();
                if (result == GetResult.Nothing) break;
                if (result == GetResult.Cancel) { CleanupBuffers(); return Result.Cancel; }

                if (result == GetResult.Option)
                {
                    var option = go.Option();
                    if (option != null)
                    {
                        if (option.EnglishName == OptMaskToggle)
                            _maskEnabled = option.CurrentListOptionIndex == 0;
                        else if (option.EnglishName == OptMaskPaintEnter && _maskEnabled)
                            EnterMaskPaintMode(doc);
                    }
                }
            }

            RhinoApp.WriteLine(T(
                $"开始雕刻: {CurrentBrushName} | 半径={_brushRadius:F1} | 强度={_brushStrength:F2}",
                $"Sculpting: {CurrentBrushName} | Radius={_brushRadius:F1} | Strength={_brushStrength:F2}"));

            // === 雕刻循环 ===
            while (true)
            {
                var optRadiusSculpt = new OptionDouble(_brushRadius, 0.1, 1000);
                var optStrengthSculpt = new OptionDouble(_brushStrength, 0.01, 1.0);
                var optMaxDispSculpt = new OptionDouble(_maxDisplacement, 0, 10000);

                var gp = new GetPoint();
                gp.SetCommandPrompt(T(
                    "点击涂抹 (Enter=完成, Esc=取消)",
                    "Click to sculpt (Enter=Done, Esc=Cancel)"));
                gp.AcceptNothing(true);

                gp.AddOptionList(OptBrush, brushNames, (int)_brushType);
                gp.AddOptionList(OptDirection, dirNames, (int)_projMode);
                gp.AddOptionList(OptFalloff, falloffNames, (int)_falloffType);
                gp.AddOptionDouble(OptRadius, ref optRadiusSculpt);
                gp.AddOptionDouble(OptStrength, ref optStrengthSculpt);
                gp.AddOptionDouble(OptMaxDisp, ref optMaxDispSculpt);

                var result = gp.Get();

                if (result == GetResult.Nothing) break;
                if (result == GetResult.Cancel)
                {
                    CancelAndRestore(doc);
                    return Result.Cancel;
                }
                if (result == GetResult.Option)
                {
                    var option = gp.Option();
                    if (option != null)
                    {
                        string optName = option.EnglishName;
                        if (optName == OptBrush)
                            _brushType = (BrushType)option.CurrentListOptionIndex;
                        else if (optName == OptDirection)
                            _projMode = (ProjectionMode)option.CurrentListOptionIndex;
                        else if (optName == OptFalloff)
                            _falloffType = (FalloffType)option.CurrentListOptionIndex;
                    }
                    _brushRadius = optRadiusSculpt.CurrentValue;
                    _brushStrength = optStrengthSculpt.CurrentValue;
                    _maxDisplacement = optMaxDispSculpt.CurrentValue;
                    _conduit.BrushRadius = _brushRadius;
                    continue;
                }
                if (result != GetResult.Point) continue;

                _brushRadius = optRadiusSculpt.CurrentValue;
                _brushStrength = optStrengthSculpt.CurrentValue;
                _maxDisplacement = optMaxDispSculpt.CurrentValue;
                _conduit.BrushRadius = _brushRadius;

                var startPoint = gp.Point();
                _strokeStartPoint = startPoint;
                _surfaceNormal = ComputeSurfaceNormal(startPoint);

                LockAffectedVertices(startPoint);
                if (_lockedCount == 0)
                {
                    RhinoApp.WriteLine(T("未命中顶点，请重试", "Missed vertices, try again."));
                    continue;
                }

                _conduit.HitPoint = startPoint;
                _conduit.IsActive = true;
                _conduit.IsDragging = true;
                _conduit.AffectedCount = _lockedCount;

                var viewport = doc.Views.ActiveView?.MainViewport;
                Vector3d camRight = viewport != null ? viewport.CameraX : Vector3d.XAxis;
                Vector3d camUp = viewport != null ? viewport.CameraY : Vector3d.YAxis;

                bool dragActive = true;
                while (dragActive)
                {
                    var gp2 = new GetPoint();
                    gp2.SetCommandPrompt(T(
                        $"[{CurrentBrushName}/{CurrentDirName}] {_lockedCount} 个顶点 — 拖拽涂抹, 点击结束",
                        $"[{CurrentBrushName}/{CurrentDirName}] {_lockedCount} vertices — Drag to sculpt, Click to end"));
                    gp2.SetBasePoint(startPoint, false);
                    gp2.PermitObjectSnap(false);
                    gp2.EnableObjectSnapCursors(false);
                    gp2.AddOptionList(OptDirection, dirNames, (int)_projMode);

                    gp2.DynamicDraw += (sender, e) =>
                    {
                        ComputeDeformation(e.CurrentPoint, camRight, camUp);
                        ApplyDeformation(doc);
                        doc.Views.Redraw();
                    };

                    var dragResult = gp2.Get();
                    if (dragResult == GetResult.Option)
                    {
                        var option = gp2.Option();
                        if (option != null && option.EnglishName == OptDirection)
                        {
                            _projMode = (ProjectionMode)option.CurrentListOptionIndex;
                            continue;
                        }
                    }
                    dragActive = false;
                }

                _conduit.IsDragging = false;
                _conduit.IsActive = false;

                CommitChanges();
                RebuildRTree();
                RhinoApp.WriteLine(T(
                    $"  ✓ 已应用 {CurrentBrushName} ({_lockedCount} 顶点)",
                    $"  ✓ Applied {CurrentBrushName} ({_lockedCount} vertices)"));
            }

            Finish(doc);
            return Result.Success;
        }

        // === 蒙版绘制子循环 ===
        private void EnterMaskPaintMode(RhinoDoc doc)
        {
            var maskFalloffNames = new string[] { NameSmoothFalloff, NameLinearFalloff, NameSharpFalloff, NameRootFalloff, NameConstantFalloff };
            var maskModes = new string[] { NameMaskPaint, NameMaskErase };

            double maskRadius = _brushRadius;
            double maskStrength = _brushStrength;
            FalloffType maskFalloff = _falloffType;

            _conduit.IsActive = true;

            while (true)
            {
                var optRadius = new OptionDouble(maskRadius, 0.1, 1000);
                var optStrength = new OptionDouble(maskStrength, 0.01, 1.0);
                var optMaxDisp = new OptionDouble(0, 0, 10000);

                var gp = new GetPoint();
                gp.SetCommandPrompt(T(
                    $"蒙版绘制:ON 半径={maskRadius:F1} 强度={maskStrength:F2} — 点击涂抹 (Enter=返回, Esc=返回)",
                    $"Mask Paint:ON Radius={maskRadius:F1} Strength={maskStrength:F2} — Click to paint (Enter=Back, Esc=Back)"));
                gp.AcceptNothing(true);

                gp.AddOptionList(OptFalloff, maskFalloffNames, (int)maskFalloff);
                gp.AddOptionDouble(OptRadius, ref optRadius);
                gp.AddOptionDouble(OptStrength, ref optStrength);
                gp.AddOptionList(OptMaskMode, maskModes, _maskEraseMode ? 1 : 0);
                gp.AddOption(OptMaskClear);
                gp.AddOption(OptMaskFill);
                gp.AddOption(OptMaskInvert);

                var result = gp.Get();

                if (result == GetResult.Nothing || result == GetResult.Cancel)
                {
                    _conduit.IsActive = false;
                    doc.Views.Redraw();
                    return;
                }

                if (result == GetResult.Option)
                {
                    var option = gp.Option();
                    if (option != null)
                    {
                        string optName = option.EnglishName;
                        if (optName == OptFalloff)
                            maskFalloff = (FalloffType)option.CurrentListOptionIndex;
                        else if (optName == OptMaskMode)
                            _maskEraseMode = option.CurrentListOptionIndex == 1;
                        else if (optName == OptMaskClear)
                        {
                            _maskData.Clear();
                            RhinoApp.WriteLine(T("  ✓ 蒙版已清除", "  ✓ Mask cleared"));
                        }
                        else if (optName == OptMaskFill)
                        {
                            _maskData.Fill();
                            RhinoApp.WriteLine(T("  ✓ 蒙版已填充", "  ✓ Mask filled"));
                        }
                        else if (optName == OptMaskInvert)
                        {
                            _maskData.Invert();
                            RhinoApp.WriteLine(T("  ✓ 蒙版已反转", "  ✓ Mask inverted"));
                        }
                    }
                    maskRadius = optRadius.CurrentValue;
                    maskStrength = optStrength.CurrentValue;
                    _conduit.BrushRadius = maskRadius;
                    continue;
                }

                if (result != GetResult.Point) continue;

                maskRadius = optRadius.CurrentValue;
                maskStrength = optStrength.CurrentValue;
                _conduit.BrushRadius = maskRadius;

                var startPoint = gp.Point();
                _strokeStartPoint = startPoint;

                LockAffectedVerticesForMask(startPoint, maskRadius, maskFalloff);
                if (_lockedCount == 0)
                {
                    RhinoApp.WriteLine(T("未命中顶点，请重试", "Missed vertices, try again."));
                    continue;
                }

                _conduit.HitPoint = startPoint;
                _conduit.IsDragging = true;
                _conduit.AffectedCount = _lockedCount;

                bool dragActive = true;
                while (dragActive)
                {
                    var gp2 = new GetPoint();
                    gp2.SetCommandPrompt(T(
                        $"蒙版绘制 {_lockedCount} 个顶点 — 拖拽涂抹, 点击结束",
                        $"Mask paint {_lockedCount} vertices — Drag to paint, Click to end"));
                    gp2.SetBasePoint(startPoint, false);
                    gp2.PermitObjectSnap(false);
                    gp2.EnableObjectSnapCursors(false);

                    gp2.DynamicDraw += (sender, e) =>
                    {
                        if (_maskEraseMode)
                            _maskData.Erase(_lockedIndices, _lockedWeights, _lockedCount, maskStrength * 0.3);
                        else
                            _maskData.Paint(_lockedIndices, _lockedWeights, _lockedCount, maskStrength * 0.3);
                        doc.Views.Redraw();
                    };

                    gp2.Get();
                    dragActive = false;
                }

                _conduit.IsDragging = false;

                RhinoApp.WriteLine(T(
                    $"  ✓ 已{(_maskEraseMode ? "擦除" : "绘制")}蒙版 ({_lockedCount} 顶点)",
                    $"  ✓ Mask {(_maskEraseMode ? "erased" : "painted")} ({_lockedCount} vertices)"));
            }
        }

        private void LockAffectedVerticesForMask(Point3d center, double radius, FalloffType falloff)
        {
            _lockedCount = 0;
            double searchRadius = radius;

            int hitCount = 0;
            _rtree.Search(new Sphere(center, searchRadius), (s, a) =>
            {
                double dist = _allBasePositions[a.Id].DistanceTo(center);
                if (dist < searchRadius)
                {
                    double nd = dist / radius;
                    if (BrushDeformer.EvaluateFalloff(falloff, nd) >= 0.001)
                        hitCount++;
                }
            });

            if (hitCount == 0) return;
            EnsureBufferSize(hitCount);

            _rtree.Search(new Sphere(center, searchRadius), (s, a) =>
            {
                int idx = a.Id;
                double dist = _allBasePositions[idx].DistanceTo(center);
                if (dist < searchRadius)
                {
                    double nd = dist / radius;
                    double w = BrushDeformer.EvaluateFalloff(falloff, nd);
                    if (w < 0.001) return;
                    _lockedIndices[_lockedCount] = idx;
                    _lockedWeights[_lockedCount] = w;
                    _lockedBasePos[_lockedCount] = _allBasePositions[idx];
                    _conduit.AffectedPoints[_lockedCount] = _allBasePositions[idx];
                    _conduit.AffectedWeights[_lockedCount] = w;
                    _lockedCount++;
                }
            });
            _conduit.AffectedCount = _lockedCount;
            RhinoApp.WriteLine($"[Debug] Final _lockedCount={_lockedCount}");
        }

        // === Mesh 专用方法 ===

        private (SculptTarget target, int localIdx) ResolveGlobalIndex(int globalIdx)
        {
            for (int ti = _targets.Count - 1; ti >= 0; ti--)
            {
                if (globalIdx >= _targets[ti].VertexOffset)
                    return (_targets[ti], globalIdx - _targets[ti].VertexOffset);
            }
            return (_targets[0], 0);
        }

        private Vector3d ComputeSurfaceNormal(Point3d point)
        {
            int closestIdx = -1;
            double closestDist = double.MaxValue;
            for (int i = 0; i < _allBasePositions.Length; i++)
            {
                double d = _allBasePositions[i].DistanceTo(point);
                if (d < closestDist) { closestDist = d; closestIdx = i; }
            }

            if (closestIdx >= 0 && closestDist < _brushRadius * 2)
            {
                try
                {
                    var (target, localIdx) = ResolveGlobalIndex(closestIdx);
                    var faceIndices = target.Mesh.Vertices.GetVertexFaces(localIdx);
                    if (faceIndices != null && faceIndices.Length > 0)
                    {
                        var avgNormal = Vector3d.Zero;
                        for (int fi = 0; fi < faceIndices.Length; fi++)
                        {
                            int fIdx = faceIndices[fi];
                            if (fIdx >= 0 && fIdx < target.Mesh.Faces.Count)
                            {
                                var n = target.Mesh.FaceNormals[fIdx];
                                if (n.Length > 0.001)
                                    avgNormal += new Vector3d(n.X, n.Y, n.Z);
                            }
                        }
                        if (avgNormal.Length > 0.001) { avgNormal.Unitize(); return avgNormal; }
                    }
                }
                catch { }
            }

            return new Vector3d(0, 0, 1);
        }

        private void LockAffectedVertices(Point3d center)
        {
            _lockedCount = 0;
            double searchRadius = _brushRadius;

            int hitCount = 0;
            _rtree.Search(new Sphere(center, searchRadius), (s, a) =>
            {
                double dist = _allBasePositions[a.Id].DistanceTo(center);
                if (dist < searchRadius)
                {
                    double nd = dist / _brushRadius;
                    if (BrushDeformer.EvaluateFalloff(_falloffType, nd) >= 0.001)
                        hitCount++;
                }
            });

            if (hitCount == 0)
            {
                _lockedCount = 0;
                return;
            }
            EnsureBufferSize(hitCount);

            _rtree.Search(new Sphere(center, searchRadius), (s, a) =>
            {
                int idx = a.Id;
                double dist = _allBasePositions[idx].DistanceTo(center);
                if (dist < searchRadius)
                {
                    double nd = dist / _brushRadius;
                    double falloff = BrushDeformer.EvaluateFalloff(_falloffType, nd);
                    if (falloff < 0.001) return;
                    _lockedIndices[_lockedCount] = idx;
                    var (target, _) = ResolveGlobalIndex(idx);
                    _lockedTargetIdx[_lockedCount] = _targets.IndexOf(target);
                    double maskVal = _maskEnabled ? _maskData.GetValue(idx) : 1.0;
                    _lockedWeights[_lockedCount] = falloff * maskVal;
                    _lockedBasePos[_lockedCount] = _allBasePositions[idx];
                    _conduit.AffectedPoints[_lockedCount] = _allBasePositions[idx];
                    _conduit.AffectedWeights[_lockedCount] = falloff * maskVal;
                    _lockedCount++;
                }
            });
            _conduit.AffectedCount = _lockedCount;
        }

        private void ComputeDeformation(Point3d currentPos, Vector3d camRight, Vector3d camUp)
        {
            _conduit.HitPoint = currentPos;

            var mouseDelta = currentPos - _strokeStartPoint;
            double dx = mouseDelta * camRight;
            double dy = mouseDelta * camUp;
            Vector3d viewDisp3D = camRight * dx + camUp * dy;

            for (int i = 0; i < _lockedCount; i++)
            {
                var basePos = _lockedBasePos[i];
                double falloff = _lockedWeights[i];
                int globalIdx = _lockedIndices[i];
                switch (_brushType)
                {
                    case BrushType.Grab:
                        _deformedPoints[i] = BrushDeformer.Grab(
                            basePos, _strokeStartPoint, currentPos,
                            falloff, _brushStrength, _projMode, _surfaceNormal,
                            viewDisp3D, viewDisp3D);
                        break;
                    case BrushType.Inflate:
                        _deformedPoints[i] = BrushDeformer.Inflate(
                            basePos, currentPos,
                            falloff, _brushStrength, _brushRadius, _projMode, _surfaceNormal);
                        break;
                    case BrushType.Smooth:
                        if (_adjacency.ContainsKey(globalIdx) && _adjacency[globalIdx].Count > 0)
                        {
                            var centroid = SubDTopologyHelper.LaplacianCenter(_allBasePositions, _adjacency[globalIdx]);
                            var delta = centroid - basePos;
                            _deformedPoints[i] = basePos + delta * _brushStrength * falloff;
                        }
                        else
                        {
                            _deformedPoints[i] = BrushDeformer.Smooth(basePos, currentPos, falloff, _brushStrength);
                        }
                        break;
                    case BrushType.Deflate:
                        _deformedPoints[i] = BrushDeformer.Deflate(
                            basePos, currentPos,
                            falloff, _brushStrength, _brushRadius, _projMode, _surfaceNormal);
                        break;
                    case BrushType.Flatten:
                        _deformedPoints[i] = BrushDeformer.Flatten(
                            basePos, currentPos,
                            falloff, _brushStrength, _projMode, _surfaceNormal);
                        break;
                    case BrushType.Pinch:
                        _deformedPoints[i] = BrushDeformer.Pinch(
                            basePos, currentPos,
                            falloff, _brushStrength, _brushRadius);
                        break;
                    case BrushType.Twist:
                        _deformedPoints[i] = BrushDeformer.Twist(
                            basePos, _strokeStartPoint, _strokeStartPoint, currentPos,
                            falloff, _brushStrength, _projMode, _surfaceNormal);
                        break;
                    case BrushType.Clay:
                        _deformedPoints[i] = BrushDeformer.Clay(
                            basePos, currentPos,
                            falloff, _brushStrength, _brushRadius,
                            _projMode, _surfaceNormal);
                        break;
                    case BrushType.Crease:
                        _deformedPoints[i] = BrushDeformer.Crease(
                            basePos, currentPos, _strokeStartPoint, currentPos,
                            falloff, _brushStrength, _brushRadius);
                        break;
                    default:
                        _deformedPoints[i] = basePos;
                        break;
                }

                if (_maxDisplacement > 0)
                {
                    var disp = _deformedPoints[i] - basePos;
                    if (disp.Length > _maxDisplacement)
                    {
                        disp.Unitize();
                        _deformedPoints[i] = basePos + disp * _maxDisplacement;
                    }
                }
            }
            _conduit.DeformedCount = _lockedCount;
        }

        private void ApplyDeformation(RhinoDoc doc)
        {
            for (int i = 0; i < _lockedCount; i++)
            {
                var target = _targets[_lockedTargetIdx[i]];
                int localIdx = _lockedIndices[i] - target.VertexOffset;
                target.Mesh.Vertices.SetVertex(localIdx, _deformedPoints[i]);
            }
            foreach (var target in _targets)
            {
                target.Mesh.Normals.ComputeNormals();
                doc.Objects.Replace(target.Id, target.Mesh);
            }
        }

        private void CommitChanges()
        {
            for (int i = 0; i < _lockedCount; i++)
            {
                int globalIdx = _lockedIndices[i];
                _allBasePositions[globalIdx] = _deformedPoints[i];
                var (target, localIdx) = ResolveGlobalIndex(globalIdx);
                target.BasePositions[localIdx] = _deformedPoints[i];
            }
        }

        private void RebuildRTree()
        {
            _rtree = new RTree();
            for (int i = 0; i < _allBasePositions.Length; i++)
                _rtree.Insert(_allBasePositions[i], i);
        }

        private void Finish(RhinoDoc doc)
        {
            foreach (var target in _targets)
            {
                target.Mesh.Normals.ComputeNormals();
                doc.Objects.Replace(target.Id, target.Mesh);
            }
            CleanupBuffers();
            doc.Views.Redraw();
            RhinoApp.WriteLine(T(
                $"=== 雕刻完成 ({_targets.Count} 个对象) ===",
                $"=== Sculpting complete ({_targets.Count} objects) ==="));
        }

        private void CancelAndRestore(RhinoDoc doc)
        {
            foreach (var target in _targets)
                doc.Objects.Replace(target.Id, target.OriginalMesh);
            CleanupBuffers();
            doc.Views.Redraw();
            RhinoApp.WriteLine(T("=== 已取消 ===", "=== Cancelled ==="));
        }

        private void CleanupBuffers()
        {
            if (_conduit != null) { _conduit.Enabled = false; _conduit = null; }
            _lockedIndices = null;
            _lockedWeights = null;
            _lockedBasePos = null;
            _lockedTargetIdx = null;
            _deformedPoints = null;
            _allBasePositions = null;
            _targets.Clear();
            _rtree = null;
            _bufferSize = 0;
        }
    }
}
