using System;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>社区频道二维码：诊断完「还是不行」时给玩家扫码求助。</summary>
    public class SusuQrForm : Form
    {
        public const string ChannelId = "pd58982960";

        public SusuQrForm(Image qr)
        {
            Text = L.T("边缘创世神社区 · 扫码加入");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(500, 724);
            BackColor = Color.FromArgb(255, 245, 250);
            Font = Fonts.F(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            PictureBox pb = new PictureBox();
            pb.Bounds = new Rectangle(14, 14, 472, 606);
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.BackColor = Color.FromArgb(30, 30, 30);
            pb.Image = qr;
            Controls.Add(pb);

            Label note = new Label();
            note.AutoSize = false;
            note.Bounds = new Rectangle(14, 626, 472, 62);
            note.Font = Fonts.F(8.5f);
            note.ForeColor = Color.FromArgb(96, 76, 88);
            note.Text = L.T("扫一扫上面的二维码，加入「边缘创世神社区」腾讯频道（频道号 ") + ChannelId + "）。\r\n"
                      + L.T("发帖求助时请带上：① 出问题时的截图  ② 【日志】页生成的诊断报告  ③ AI 诊断建议（如果有）");
            Controls.Add(note);

            ZButton copy = new ZButton();
            copy.Text = L.T("复制频道号");
            copy.Bounds = new Rectangle(180, 690, 130, 26);
            copy.SetFont(Fonts.F(8.5f));
            copy.Radius = 13;
            copy.BorderColor = Color.FromArgb(244, 154, 186);
            copy.TextColor = Color.FromArgb(198, 62, 112);
            copy.CornerColor = Color.FromArgb(255, 245, 250);
            copy.Click += delegate(object s, EventArgs e)
            {
                try { Clipboard.SetText(ChannelId); MinMsg.Show(this, L.T("频道号已复制：") + ChannelId, L.T("已复制"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch { }
            };
            Controls.Add(copy);

            ZButton close = new ZButton();
            close.Text = L.T("关闭");
            close.Bounds = new Rectangle(320, 690, 100, 26);
            close.SetFont(Fonts.F(8.5f));
            close.Radius = 13;
            close.BorderColor = Color.FromArgb(214, 196, 206);
            close.TextColor = Color.FromArgb(140, 110, 124);
            close.CornerColor = Color.FromArgb(255, 245, 250);
            close.Click += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(close);
        }
    }
}
