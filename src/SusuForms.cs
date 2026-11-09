using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>苏苏的选项窗（也是"教程目录"）。</summary>
    public class SusuChoiceForm : Form
    {
        public string Picked = "";
        public string PickedAction = "";
        public bool Dismissed;

        public SusuChoiceForm(List<GuideChoice> choices, string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(255, 244, 249);
            Font = Fonts.F(9f);
            int n = choices == null ? 0 : choices.Count;
            int rowH = 40;
            int top = 58;
            int h = top + n * rowH + 56;
            if (h > 620) h = 620;
            ClientSize = new Size(520, h);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label lb = new Label();
            lb.Text = title;
            lb.Font = Fonts.B(11f);
            lb.ForeColor = Color.FromArgb(198, 62, 112);
            lb.AutoSize = false;
            lb.Bounds = new Rectangle(20, 16, 480, 28);
            Controls.Add(lb);

            int y = top;
            for (int i = 0; i < n; i++)
            {
                GuideChoice c = choices[i];
                ZButton b = new ZButton();
                b.Text = c.Label + (c.Disabled && !string.IsNullOrEmpty(c.Note) ? "  （" + c.Note + "）" : "");
                b.Bounds = new Rectangle(20, y, 480, 34);
                b.SetFont(Fonts.F(9f));
                b.Radius = 8;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.BorderColor = c.Disabled ? Color.FromArgb(220, 210, 216) : Color.FromArgb(244, 154, 186);
                b.TextColor = c.Disabled ? Color.FromArgb(170, 160, 166) : Color.FromArgb(160, 40, 88);
                b.BackNormal = Color.White;
                b.BackHover = Color.FromArgb(255, 238, 246);
                b.BackDown = Color.FromArgb(255, 224, 238);
                b.CornerColor = Color.FromArgb(255, 244, 249);
                b.Enabled = !c.Disabled;
                string next = c.Next;
                string act = c.Action;
                b.Click += delegate(object s, EventArgs e)
                {
                    Picked = next;
                    PickedAction = act;
                    Close();
                };
                Controls.Add(b);
                y += rowH;
            }

            ZButton close = new ZButton();
            close.Text = L.T("先不看了");
            close.Bounds = new Rectangle(20, h - 44, 130, 30);
            close.SetFont(Fonts.F(8.5f));
            close.Radius = 15;
            close.BorderColor = Color.FromArgb(220, 200, 210);
            close.TextColor = Color.FromArgb(150, 120, 132);
            close.CornerColor = Color.FromArgb(255, 244, 249);
            close.Click += delegate(object s, EventArgs e) { Dismissed = true; Close(); };
            Controls.Add(close);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }
    }

    /// <summary>苏苏说过的话（记录），可以往回翻。</summary>
    public class SusuBacklogForm : Form
    {
        public SusuBacklogForm(List<string> history) : this(history, L.T("苏苏说过的话")) { }
        public SusuBacklogForm(List<string> history, string title)
        {
            Text = string.IsNullOrEmpty(title) ? L.T("苏苏说过的话") : title;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 480);
            MinimumSize = new Size(360, 260);
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = Fonts.F(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TextBox t = new TextBox();
            t.Multiline = true;
            t.ReadOnly = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.WordWrap = true;
            t.BorderStyle = BorderStyle.None;
            t.Font = Fonts.F(9.5f);
            t.BackColor = Color.White;
            t.ForeColor = Color.FromArgb(64, 44, 58);
            t.Dock = DockStyle.Fill;
            if (history != null && history.Count > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < history.Count; i++)
                {
                    sb.AppendLine(history[i]);
                    sb.AppendLine();
                }
                t.Text = sb.ToString();
            }
            else t.Text = L.T("（还没说什么呢）");
            t.SelectionStart = t.TextLength;
            t.ScrollToCaret();
            Controls.Add(t);
        }
    }
}
