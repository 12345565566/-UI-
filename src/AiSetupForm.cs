using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>配置 AI 接口：地址 / KEY / 模型。</summary>
    public class AiSetupForm : Form
    {
        public bool Saved;
        private TextBox _tBase, _tKey;
        private ComboBox _cModel;
        private Label _lbStatus;
        private ZButton _bFetch, _bSave, _bCancel;

        public AiSetupForm(Settings st)
        {
            Text = L.T("苏苏的 AI 诊断 · 接口配置");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(600, 400);
            BackColor = Color.FromArgb(255, 245, 250);
            Font = Fonts.F(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = MkLabel(L.T("苏苏的 AI 诊断 · 接口配置"), Fonts.B(12f), Color.FromArgb(198, 62, 112));
            title.Bounds = new Rectangle(22, 14, 556, 28);
            Controls.Add(title);

            Label note = MkLabel(
                L.T("支持任何「OpenAI 兼容」接口：官方 api.openai.com、各类中转站、本地 ollama / LM Studio 都行。") +
                L.T("\r\n地址填到 /v1 这一层即可，例如 https://api.openai.com/v1 。"),
                Fonts.F(8.5f), Color.FromArgb(120, 96, 108));
            note.Bounds = new Rectangle(22, 44, 556, 44);
            Controls.Add(note);

            Controls.Add(MkLabel(L.T("API 地址"), Fonts.F(9f), Color.FromArgb(90, 70, 82), 22, 106, 80));
            _tBase = MkBox(110, 104, 466);
            _tBase.Text = string.IsNullOrEmpty(st.ApiBase) ? "https://api.openai.com/v1" : st.ApiBase;
            Controls.Add(_tBase);

            Controls.Add(MkLabel("API KEY", Fonts.F(9f), Color.FromArgb(90, 70, 82), 22, 150, 80));
            _tKey = MkBox(110, 148, 466);
            _tKey.UseSystemPasswordChar = true;
            _tKey.Text = st.ApiKey == null ? "" : st.ApiKey;
            Controls.Add(_tKey);

            Controls.Add(MkLabel(L.T("模型"), Fonts.F(9f), Color.FromArgb(90, 70, 82), 22, 194, 80));
            _cModel = new ComboBox();
            _cModel.Bounds = new Rectangle(110, 192, 340, 26);
            _cModel.DropDownStyle = ComboBoxStyle.DropDown;
            _cModel.Font = Fonts.F(9f);
            _cModel.Text = st.ApiModel == null ? "" : st.ApiModel;
            Controls.Add(_cModel);

            _bFetch = new ZButton();
            _bFetch.Text = L.T("获取模型列表");
            _bFetch.Bounds = new Rectangle(458, 191, 118, 28);
            _bFetch.SetFont(Fonts.F(8.5f));
            _bFetch.Radius = 8;
            _bFetch.BorderColor = Color.FromArgb(244, 154, 186);
            _bFetch.TextColor = Color.FromArgb(198, 62, 112);
            _bFetch.CornerColor = Color.FromArgb(255, 245, 250);
            _bFetch.Click += delegate(object s, EventArgs e) { FetchModels(); };
            Controls.Add(_bFetch);

            _lbStatus = MkLabel(L.T("先填地址和 KEY，点【获取模型列表】验证能不能连上。"), Fonts.F(8.5f), Color.FromArgb(140, 110, 124));
            _lbStatus.Bounds = new Rectangle(22, 232, 556, 60);
            Controls.Add(_lbStatus);

            Label tip = MkLabel(
                L.T("★ 点【保存并诊断】时，启动器会把「启动器日志 + 游戏日志里的报错 + 本机环境」发给这个接口做分析。") +
                L.T("\r\n   如果不想上传，就点取消，用本地体检。"),
                Fonts.F(8f), Color.FromArgb(170, 130, 145));
            tip.Bounds = new Rectangle(22, 296, 556, 44);
            Controls.Add(tip);

            _bSave = new ZButton();
            _bSave.Text = L.T("保存并诊断");
            _bSave.Bounds = new Rectangle(340, 350, 120, 34);
            _bSave.SetFont(Fonts.B(9f));
            _bSave.Radius = 8;
            _bSave.BorderColor = Color.FromArgb(226, 74, 130);
            _bSave.TextColor = Color.White;
            _bSave.BackNormal = Color.FromArgb(226, 74, 130);
            _bSave.BackHover = Color.FromArgb(238, 96, 148);
            _bSave.BackDown = Color.FromArgb(200, 60, 112);
            _bSave.CornerColor = Color.FromArgb(255, 245, 250);
            _bSave.Click += delegate(object s, EventArgs e) { DoSave(st); };
            Controls.Add(_bSave);

            _bCancel = new ZButton();
            _bCancel.Text = L.T("取消");
            _bCancel.Bounds = new Rectangle(470, 350, 100, 34);
            _bCancel.SetFont(Fonts.F(9f));
            _bCancel.Radius = 8;
            _bCancel.BorderColor = Color.FromArgb(214, 196, 206);
            _bCancel.TextColor = Color.FromArgb(140, 110, 124);
            _bCancel.CornerColor = Color.FromArgb(255, 245, 250);
            _bCancel.Click += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(_bCancel);
        }

        private Label MkLabel(string text, Font f, Color c, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text; l.Font = f; l.ForeColor = c;
            l.AutoSize = false; l.Bounds = new Rectangle(x, y, w, 26);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private Label MkLabel(string text, Font f, Color c)
        {
            Label l = new Label();
            l.Text = text; l.Font = f; l.ForeColor = c;
            l.AutoSize = false;
            return l;
        }

        private TextBox MkBox(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Bounds = new Rectangle(x, y, w, 24);
            t.Font = Fonts.F(9f);
            t.BorderStyle = BorderStyle.FixedSingle;
            return t;
        }

        private void FetchModels()
        {
            string err;
            _lbStatus.ForeColor = Color.FromArgb(140, 110, 124);
            _lbStatus.Text = L.T("正在连接 ") + AiClient.Normalize(_tBase.Text) + " …";
            Application.DoEvents();
            List<string> models = AiClient.ListModels(_tBase.Text, _tKey.Text.Trim(), out err);
            if (err != null || models == null || models.Count == 0)
            {
                _lbStatus.ForeColor = Color.FromArgb(200, 60, 60);
                _lbStatus.Text = L.T("连接失败：") + (err == null ? L.T("没有可用模型") : err);
                return;
            }
            _cModel.Items.Clear();
            for (int i = 0; i < models.Count; i++) _cModel.Items.Add(models[i]);
            if (string.IsNullOrEmpty(_cModel.Text) && models.Count > 0) _cModel.Text = models[0];
            _lbStatus.ForeColor = Color.FromArgb(40, 150, 90);
            _lbStatus.Text = L.T("连接成功，共 ") + models.Count + L.T(" 个模型。选一个再点【保存并诊断】。");
        }

        private void DoSave(Settings st)
        {
            string b = AiClient.Normalize(_tBase.Text);
            if (b.Length == 0) { _lbStatus.ForeColor = Color.FromArgb(200, 60, 60); _lbStatus.Text = L.T("API 地址不能为空。"); return; }
            if (string.IsNullOrEmpty(_cModel.Text.Trim())) { _lbStatus.ForeColor = Color.FromArgb(200, 60, 60); _lbStatus.Text = L.T("请先点【获取模型列表】并选一个模型。"); return; }
            st.ApiBase = b;
            st.ApiKey = _tKey.Text.Trim();
            st.ApiModel = _cModel.Text.Trim();
            st.Save();
            Saved = true;
            Close();
        }
    }
}
