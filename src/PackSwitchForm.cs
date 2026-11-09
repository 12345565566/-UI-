using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 一键切换整合包。
    /// 列出所有导入过的包（含 1.6 / 1.7），点一下就把环境切过去。
    /// </summary>
    public class PackSwitchForm : Form
    {
        private ListBox _list;
        private Button _ok, _cancel, _del;
        private Label _hint;
        private List<PackEntry> _items = new List<PackEntry>();
        private string _currentId = "";

        public PackEntry Selected;

        public PackSwitchForm(List<PackEntry> items, string currentPackId)
        {
            _currentId = currentPackId == null ? "" : currentPackId;
            Text = L.T("切换整合包");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 420);
            BackColor = Color.White;
            Font = new Font(L.T("微软雅黑"), 9f);

            Label title = new Label();
            title.Text = L.T("选择一个整合包");
            title.Font = new Font(L.T("微软雅黑"), 11f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(40, 44, 52);
            title.Bounds = new Rectangle(18, 14, 400, 26);
            Controls.Add(title);

            _hint = new Label();
            _hint.Text = L.T("切换会把两边的配置各存一份（版本隔离），1.6 和 1.7 不会互相覆盖。");
            _hint.ForeColor = Color.FromArgb(120, 126, 138);
            _hint.Bounds = new Rectangle(18, 40, 520, 20);
            Controls.Add(_hint);

            _list = new ListBox();
            _list.Bounds = new Rectangle(18, 66, 524, 292);
            _list.IntegralHeight = false;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 40;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.DrawItem += new DrawItemEventHandler(DrawRow);
            _list.DoubleClick += delegate(object s, EventArgs e) { Accept(); };
            _list.SelectedIndexChanged += delegate(object s, EventArgs e) { UpdateButtons(); };
            Controls.Add(_list);

            _ok = new Button();
            _ok.Text = L.T("切换到这个包");
            _ok.Bounds = new Rectangle(336, 372, 118, 32);
            _ok.Click += delegate(object s, EventArgs e) { Accept(); };
            Controls.Add(_ok);

            _del = new Button();
            _del.Text = L.T("从列表移除");
            _del.Bounds = new Rectangle(206, 372, 118, 32);
            _del.Click += delegate(object s, EventArgs e) { RemoveSelected(); };
            Controls.Add(_del);

            _cancel = new Button();
            _cancel.Text = L.T("取消");
            _cancel.Bounds = new Rectangle(462, 372, 80, 32);
            _cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(_cancel);

            SetList(items);
        }

        private void SetList(List<PackEntry> items)
        {
            _items = items == null ? new List<PackEntry>() : items;
            _list.Items.Clear();
            int sel = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                PackEntry e = _items[i];
                string mark = string.Equals(e.PackId, _currentId, StringComparison.OrdinalIgnoreCase) ? "▶ " : "   ";
                string tail = e.Missing ? L.T("（文件夹找不到了）") : (e.Mods + L.T(" 个模组"));
                _list.Items.Add(mark + "[" + e.VersionText + "]  " + e.DisplayName + "    " + tail);
                if (string.Equals(e.PackId, _currentId, StringComparison.OrdinalIgnoreCase)) sel = i;
            }
            if (sel < 0 && _list.Items.Count > 0) sel = 0;
            if (sel >= 0) _list.SelectedIndex = sel;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int i = _list.SelectedIndex;
            bool has = i >= 0 && i < _items.Count;
            _ok.Enabled = has && !_items[i].Missing
                && !string.Equals(_items[i].PackId, _currentId, StringComparison.OrdinalIgnoreCase);
            _del.Enabled = has && !string.Equals(_items[i].PackId, _currentId, StringComparison.OrdinalIgnoreCase);
        }

        private void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _items.Count) return;
            PackEntry en = _items[e.Index];
            bool current = string.Equals(en.PackId, _currentId, StringComparison.OrdinalIgnoreCase);
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            Color back = sel ? Color.FromArgb(255, 240, 246) : Color.White;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);

            Color fg = en.Missing ? Color.FromArgb(170, 170, 170) : Color.FromArgb(38, 42, 50);
            Font fb = new Font(L.T("微软雅黑"), 10f, current ? FontStyle.Bold : FontStyle.Regular);
            Font fs = new Font(L.T("微软雅黑"), 8f);
            Color badgeBack = en.VersionText == "1.6" ? Color.FromArgb(255, 236, 209) : Color.FromArgb(220, 238, 255);
            Color badgeFg = en.VersionText == "1.6" ? Color.FromArgb(160, 100, 10) : Color.FromArgb(30, 100, 170);

            Rectangle bb = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 9, 46, 22);
            using (SolidBrush b = new SolidBrush(badgeBack)) e.Graphics.FillRectangle(b, bb);
            TextRenderer.DrawText(e.Graphics, en.Badge, fs, bb, badgeFg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle nb = new Rectangle(e.Bounds.X + 64, e.Bounds.Y + 6, e.Bounds.Width - 74, 20);
            TextRenderer.DrawText(e.Graphics, en.DisplayName, fb, nb, fg,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            string sub = en.Missing ? L.T("文件夹找不到了：") + en.Root : (en.Mods + L.T(" 个模组 · ") + en.Root);
            Rectangle sb = new Rectangle(e.Bounds.X + 64, e.Bounds.Y + 24, e.Bounds.Width - 74, 18);
            TextRenderer.DrawText(e.Graphics, sub, fs, sb, Color.FromArgb(140, 146, 158),
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            fb.Dispose();
            fs.Dispose();
        }

        private void Accept()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            if (_items[i].Missing)
            {
                MinMsg.Show(this, L.T("这个包的文件夹已经不在了：\r\n") + _items[i].Root, L.T("切换不了"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Selected = _items[i];
            DialogResult = DialogResult.OK;
        }

        private void RemoveSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            PackEntry en = _items[i];
            if (MinMsg.Show(this, L.T("把「") + en.DisplayName + L.T("」从列表里移除？\r\n\r\n只移除记录，不会删掉磁盘上的文件。"),
                    L.T("确认"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _items.RemoveAt(i);
            PackRegistry.Save(SettingsDirOf(), _items);
            SetList(_items);
        }

        private string SettingsDirOf()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
        }
    }
}
