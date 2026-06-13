using System;
using Eto.Forms;
using Eto.Drawing;
using Rhino;

namespace SubDSculptTest
{
    /// <summary>
    /// 雕刻工具面板: 点击按钮启动 SculptBrush 命令
    /// </summary>
    [System.Runtime.InteropServices.Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901")]
    public class SculptPanel : Panel
    {
        public SculptPanel()
        {
            var btnSculpt = new Button { Text = "🖌 开始雕刻", Width = 160, Height = 40 };
            btnSculpt.Click += (s, e) =>
            {
                RhinoApp.RunScript("_SculptBrush", false);
            };

            var btnHelp = new Button { Text = "❓ 快捷键说明", Width = 160, Height = 30 };
            btnHelp.Click += (s, e) =>
            {
                RhinoApp.WriteLine("=== SculptBrush 雕刻笔刷 ===");
                RhinoApp.WriteLine("笔刷(B): 抓取/膨胀/平滑/收缩/展平/夹捏/扭转/堆积/折痕/蒙版");
                RhinoApp.WriteLine("方向(D): 视图/法线/X轴/Y轴/Z轴");
                RhinoApp.WriteLine("衰减(F): 平滑/线性/锐利/根号/硬边");
                RhinoApp.WriteLine("半径(R): 0.1~1000");
                RhinoApp.WriteLine("强度(S): 0.01~1.0");
                RhinoApp.WriteLine("蒙版(M): 绘制/擦除");
                RhinoApp.WriteLine("Enter=确认, Esc=取消");
            };

            var layout = new DynamicLayout { Padding = new Padding(10), Spacing = new Size(5, 8) };
            layout.Add(new Label { Text = "SubD 雕刻工具", Font = new Font(SystemFont.Bold, 12) });
            layout.Add(btnSculpt);
            layout.Add(btnHelp);
            layout.Add(null); // spacer
            layout.Add(new Label { Text = "输入 SculptBrush 命令\n或点击上方按钮开始", TextColor = Colors.Gray });

            Content = layout;
        }
    }
}
