using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace P2PChat
{
    /// <summary>Главное окно. Запустил — ввёл ник — всё, дальше само: ищет соседей, подключается.</summary>
    public class MainForm : Form
    {
        private readonly NetworkManager _net = new NetworkManager();
        private readonly RichTextBox _chat = new RichTextBox();
        private readonly TextBox _input = new TextBox();
        private readonly Label _status = new Label();
        private readonly ListBox _peers = new ListBox();
        private readonly Dictionary<string, IPEndPoint> _peerEps = new();
        private bool _firstRun = true;

        public MainForm()
        {
            Text = "P2P Чат — без серверов";
            Width = 760; Height = 560;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 420);
            BackColor = Color.FromArgb(30, 32, 40);
            Font = new Font("Segoe UI", 10f);

            // ---- левая колонка: чат ----
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            _status.Dock = DockStyle.Fill;
            _status.ForeColor = Color.FromArgb(120, 200, 255);
            _status.AutoSize = false;
            _status.Height = 24;
            layout.Controls.Add(_status, 0, 0);
            layout.SetColumnSpan(_status, 2);

            _chat.ReadOnly = true;
            _chat.BackColor = Color.FromArgb(24, 26, 33);
            _chat.ForeColor = Color.Gainsboro;
            _chat.BorderStyle = BorderStyle.None;
            _chat.Dock = DockStyle.Fill;
            layout.Controls.Add(_chat, 0, 1);

            _peers.Dock = DockStyle.Fill;
            _peers.BackColor = Color.FromArgb(24, 26, 33);
            _peers.ForeColor = Color.Gainsboro;
            _peers.BorderStyle = BorderStyle.None;
            layout.Controls.Add(_peers, 1, 1);

            _input.PlaceholderText = "Сообщение... (Enter — отправить)";
            _input.Dock = DockStyle.Fill;
            _input.Font = new Font("Segoe UI", 11f);
            _input.KeyDown += Input_KeyDown;
            var sendBtn = new Button
            {
                Text = "➤",
                Dock = DockStyle.Right,
                Width = 48,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 120, 210),
                ForeColor = Color.White
            };
            sendBtn.FlatAppearance.BorderSize = 0;
            sendBtn.Click += (s, e) => SendMessage();
            layout.Controls.Add(_input, 0, 2);
            layout.Controls.Add(sendBtn, 1, 2);

            Controls.Add(layout);

            // ---- подписки на сеть ----
            _net.OnChatMessage += (nick, text) => AppendLine(nick + ": ", text, Color.Gainsboro);
            _net.OnLog += s => AppendLine("", s, Color.DimGray);
            _net.OnPeerConnected += (nick, ep) => AddPeer(nick, ep);
            _net.OnPeerDisconnected += RemovePeer;
            _net.OnPeerDiscovered += (nick, ep) => { _peerEps[nick] = ep; };
            FormClosing += (s, e) => _net.Dispose();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_firstRun) return;
            _firstRun = false;

            string nick = AskNickname();
            _net.Nickname = string.IsNullOrWhiteSpace(nick) ? Environment.UserName : nick.Trim();
            try { _net.Start(); }
            catch (Exception ex) { AppendLine("", "Ошибка запуска сети: " + ex.Message, Color.OrangeRed); }
            UpdateStatus();
            AppendLine("", "Ищем соседей в локальной сети автоматически...", Color.FromArgb(120, 200, 255));
            _input.Focus();
        }

        private string AskNickname()
        {
            // простой диалог ввода ника — одна строка, без лишних окон
            using var dlg = new Form
            {
                Text = "Кто вы?",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(320, 120),
                MaximizeBox = false, MinimizeBox = false
            };
            var lbl = new Label { Text = "Ваш ник:", Left = 12, Top = 16, AutoSize = true };
            var tb = new TextBox { Left = 90, Top = 13, Width = 210, Text = Environment.UserName };
            var ok = new Button { Text = "ОК", Left = 120, Top = 60, Width = 80, DialogResult = DialogResult.OK };
            dlg.Controls.AddRange(new Control[] { lbl, tb, ok });
            dlg.AcceptButton = ok;
            return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendMessage(); }
        }

        private void SendMessage()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            _input.Clear();
            AppendLine(_net.Nickname + " (вы): ", text, Color.FromArgb(130, 230, 130));
            _net.Broadcast(text);
        }

        private void AddPeer(string nick, IPEndPoint ep)
        {
            if (ep != null) _peerEps[nick] = ep;
            if (!_peers.Items.Contains(nick))
            {
                _peers.Items.Add(nick);
                UpdateStatus();
            }
        }

        private void RemovePeer(string nick)
        {
            if (_peers.Items.Contains(nick))
            {
                _peers.Items.Remove(nick);
                _peerEps.Remove(nick);
                UpdateStatus();
            }
        }

        private void UpdateStatus()
        {
            int n = _peers.Items.Count;
            _status.Text = "Онлайн соседей: " + n + "   |   Ваш порт: " + NetworkManager.DefaultTcpPort;
        }

        private void AppendLine(string prefix, string text, Color color)
        {
            if (IsHandleCreated && InvokeRequired) { BeginInvoke(new Action(() => AppendLine(prefix, text, color))); return; }
            _chat.SelectionStart = _chat.TextLength;
            _chat.SelectionColor = Color.Gray;
            _chat.AppendText(DateTime.Now.ToString("HH:mm ") + prefix);
            _chat.SelectionColor = color;
            _chat.AppendText(text + "\n");
            _chat.ScrollToCaret();
        }
    }
}
