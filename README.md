# Rhino 8 交互式雕刻笔刷插件

基于 RhinoCommon 8.x 的交互式雕刻插件，支持 SubD、Mesh、NURBS 曲面的实时笔刷雕刻。

## 功能

- **9 种笔刷类型**：抓取、膨胀、平滑、收缩、展平、夹捏、扭转、堆积、折痕
- **5 种投影方向**：视图、法线、X轴、Y轴、Z轴
- **5 种衰减曲线**：平滑、线性、锐利、根号、硬边
- **蒙版系统**：绘制/擦除蒙版，保护/暴露区域，支持清除/填充/反转
- **多对象支持**：同时选择多个对象进行雕刻
- **实时预览**：拖拽过程中实时显示变形效果
- **撤销恢复**：Esc 取消所有修改，恢复原始几何体

## 命令

| 命令 | 说明 |
|------|------|
| `SculptBrush` | SubD 细分体雕刻 |
| `MeshSculptBrush` | Mesh 网格雕刻 |
| `NurbsSculptBrush` | NURBS 曲面雕刻（修改控制点） |

## 项目结构

```
├── SubDSculptTestPlugin.cs      # 插件入口，注册命令
├── InteractiveBrushCommand.cs   # SubD 雕刻命令主逻辑
├── MeshSculptBrushCommand.cs    # Mesh 雕刻命令
├── NurbsSculptBrushCommand.cs   # NURBS 曲面雕刻命令
├── BrushDeformer.cs             # 笔刷变形数学计算（纯函数）
├── BrushDisplayConduit.cs       # 显示管道：笔刷光标、顶点预览、蒙版覆盖
├── SubDTopologyHelper.cs        # 拓扑辅助：邻域关系、Laplacian 平滑
├── MaskData.cs                  # 蒙版数据存储与操作
├── SculptPanel.cs               # 面板（未启用）
├── Test*.cs                     # 测试命令
└── Properties/AssemblyInfo.cs   # 程序集信息
```

## 核心架构

### 数据流

```
用户点击/拖拽
    │
    ▼
GetPoint (Rhino 输入)
    │
    ▼
LockAffectedVertices()  ──→  RTree 空间查询
    │                         找到笔刷范围内的顶点
    ▼
ComputeDeformation()    ──→  BrushDeformer 计算位移
    │                         根据笔刷类型、方向、衰减
    ▼
ApplyDeformation()      ──→  更新文档几何体
    │                         doc.Objects.Replace()
    ▼
BrushDisplayConduit     ──→  实时绘制预览
                              笔刷圆环 + 顶点颜色 + 蒙版
```

### 文件职责

**`BrushDeformer.cs`** — 纯数学计算，无状态
- `EvaluateFalloff(type, t)` — 衰减曲线求值
- `Grab / Inflate / Smooth / ...` — 各笔刷类型的位移计算
- `GetDirection(proj, normal)` — 投影方向向量

**`SubDTopologyHelper.cs`** — 拓扑关系
- `BuildAdjacency(vertices, subd)` — SubD 顶点邻域（通过边遍历）
- `BuildMeshAdjacency(mesh)` — Mesh 顶点邻域
- `LaplacianCenter(positions, neighbors)` — Laplacian 重心计算

**`BrushDisplayConduit.cs`** — DisplayConduit 子类
- `PostDrawObjects` 中绘制：笔刷球体、十字准星、受影响顶点（红→绿）、变形预览（绿色）、蒙版叠加层

**`MaskData.cs`** — 蒙版数据
- `Paint / Erase` — 拖拽中实时修改蒙版值
- `Clear / Fill / Invert` — 批量操作
- `DrawOverlay` — 蒙版可视化（红=保护，绿=暴露）

**`*Command.cs`** — 命令主逻辑
- 第一级：对象选择 + 蒙版开关
- 雕刻循环：GetPoint + 选项 + 拖拽
- 蒙版绘制子循环：独立的 GetPoint 循环

### 关键设计决策

1. **直接修改原始几何体**（非工作副本）— 避免隐藏/删除对象的副作用
2. **拖拽中实时 Replace** — 通过 `doc.Objects.Replace()` 更新文档，conduit 绘制额外预览
3. **RTree 空间索引** — 快速查找笔刷范围内的顶点
4. **全局顶点数组** — 多对象时合并为统一数组，通过 `VertexOffset` 映射回各对象
5. **`AcceptNothing(true)` 而非 `AcceptEnterWhenDone(true)`** — 避免点击被误解释为 Enter

## 构建

### 环境

- Visual Studio 2022
- .NET Framework 4.8
- Rhino 8

### 步骤

```bash
# 克隆仓库
git clone https://github.com/YOUR_USERNAME/rhino-sculpt-plugin.git
cd rhino-sculpt-plugin

# 构建
dotnet build -c Release

# 输出: bin/Release/net48/SubDSculptTest.rhp
```

### 安装

1. 构建后将 `SubDSculptTest.rhp` 复制到 Rhino 插件目录
2. 或在 Rhino 中 `_PlugInManager` → 安装 → 选择 `.rhp` 文件
3. 重启 Rhino，输入 `SculptBrush` / `MeshSculptBrush` / `NurbsSculptBrush`


## 依赖

- [RhinoCommon 8.x](https://www.nuget.org/packages/RhinoCommon) — Rhino API
- [Eto.Forms 2.8.x](https://www.nuget.org/packages/Eto.Forms) — UI 框架（当前未使用）

## 许可证

MIT
