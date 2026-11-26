using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SootKeys
{
    internal sealed partial class CatToolbox : Form
    {
        static readonly string[] Moves = {
            "收爪蹲好", "左爪拍键", "右爪拍键", "后脚踩空格", "后脚够回车", "伸手够鼠标", "四爪掀键帽"
        };
        static readonly string[] Fortunes = {
            "今日宜撤回。尾巴刚才打的那行字不算。", "遇事先伸懒腰。问题也许比你先累。",
            "你的空格键今天有福，分到一只热乎乎的后脚。", "此签可兑换一次理直气壮的发呆。",
            "今天的灵感会从键盘缝里探出头。", "卡住时喝口水。我替你盯着那个光标。",
            "小步也算步。猫步更算。", "刚刚那声噼啪，是好运在敲回车。",
            "本日幸运姿势：前爪收好，后脚加班。", "适合把大难题切成猫饼干那么小。",
            "想不出名字的变量，先放到窗边晒晒。", "你的进度条悄悄长了一根胡须。",
            "偷个小懒吧，我帮你演出一副很忙的样子。", "今晚的月亮有点像掉到地上的键帽。",
            "慢慢来。猫连坐下都要先转半圈。", "墨爪盖章：今天有一件小事会做得很顺。"
        };
        static readonly string[] Quips = {
            "谁又把空格键装在了我脚底？", "我数过了，四只爪子够用，再多是管理问题。",
            "别看我。我只负责批准键帽起飞。", "光标一闪一闪的，我差点就扑上去了。",
            "刚才那串乱码是尾巴提交的。", "我打字靠手速，撤回靠后脚。"
        };
        readonly Action<Pose> audition;
        readonly CatToolboxData data;
        readonly Random random = new Random();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer tick = new Timer { Interval = 200 };
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly Label feedback = new Label { Dock = DockStyle.Bottom, Height = 42,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 2, 14, 2) };
        readonly TextBox search = new TextBox { Width = 160 };
        readonly ComboBox filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
        readonly ListBox moves = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly List<int> visibleMoves = new List<int>();
        readonly Label fortune = Paragraph();
        readonly ListBox kept = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        Button favoriteMove, keepFortune;
        int currentFortune = -1;
        bool shuttingDown;

        internal CatToolbox(Action<Pose> audition) : this(audition, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SootKeys")) { }

        internal CatToolbox(Action<Pose> audition, string directory)
        {
            this.audition = audition;
            data = new CatToolboxData(directory);
            Text = "墨爪百宝箱";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Color.FromArgb(246, 246, 237);
            ClientSize = new Size(520, 402);
            MinimumSize = new Size(470, 380);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            KeyPreview = true;
            Controls.Add(tabs);
            Controls.Add(feedback);
            BuildActions();
            BuildStories();
            BuildFocus();
            BuildFortunes();
            BuildGame();
            tabs.SelectedIndexChanged += delegate {
                if (tabs.SelectedTab != gamePage) PauseGame();
                if (tabs.SelectedTab != storyPage) PauseStory();
                string[] hints = { "双击动作可点播，按 / 搜索。", "墨爪在桌面演出，台词显示在这里。",
                    "关上百宝箱仍会计时；退出墨爪会保存并暂停。", "最近三次抽到的签会避开。",
                    "用鼠标点泡泡，或聚焦泡泡后按空格。" };
                if (tabs.SelectedIndex >= 0) Status(hints[tabs.SelectedIndex]);
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) PausePanelGames(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (!shuttingDown && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    PausePanelGames();
                    Hide();
                }
            };
            tick.Tick += delegate { Pulse(); };
            tick.Start();
            Status(data.Remaining > 0 ? "上次的专注已暂停，可在「专注」里继续。" : "选一个动作，叫墨爪演给你看。");
        }

        internal void Open()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
        }

        internal void Quip()
        {
            Open();
            Status(Quips[random.Next(Quips.Length)]);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.OemQuestion && tabs.SelectedIndex == 0 && !search.Focused) {
                search.Focus(); search.SelectAll(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal void PausePanelGames() { PauseGame(); PauseStory(); }
        void Status(string value) { feedback.Text = value; }
        void Save()
        {
            data.Save();
            if (!data.CouldSave) Status("本机暂时无法保存记录，本次操作仍有效。");
        }
        static Label Paragraph()
        {
            return new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8), AutoEllipsis = true };
        }
        static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true,
                Padding = Padding.Empty, Margin = Padding.Empty };
        }
        static Button Command(Control parent, string title, Action handler)
        {
            Button button = new Button { Text = title, AutoSize = true, MinimumSize = new Size(58, 29),
                UseVisualStyleBackColor = true, Margin = new Padding(2, 2, 4, 2) };
            button.Click += delegate { handler(); };
            parent.Controls.Add(button);
            return button;
        }
        TabPage Page(string title, out TableLayoutPanel layout)
        {
            TabPage page = new TabPage(title) { Padding = new Padding(10), UseVisualStyleBackColor = true };
            layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);
            tabs.TabPages.Add(page);
            return page;
        }
        static void Add(TableLayoutPanel layout, Control content, bool fill)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(content, 0, row);
        }

        void BuildActions()
        {
            TableLayoutPanel layout;
            Page("动作", out layout);
            var bar = Row();
            bar.Controls.Add(new Label { Text = "查找动作", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            bar.Controls.Add(search);
            filter.Items.AddRange(new object[] { "全部", "已收藏", "最近点播" });
            bar.Controls.Add(filter);
            Add(layout, bar, false);
            Add(layout, moves, true);
            var buttons = Row();
            Command(buttons, "播放", PlaySelected);
            favoriteMove = Command(buttons, "收藏", delegate {
                int id = SelectedMove();
                if (id < 0) return;
                if (!data.Favorites.Remove(id)) data.Favorites.Add(id);
                RefreshMoves(); Save();
            });
            Add(layout, buttons, false);
            moves.DoubleClick += delegate { PlaySelected(); };
            moves.SelectedIndexChanged += delegate {
                int id = SelectedMove();
                favoriteMove.Enabled = id >= 0;
                favoriteMove.Text = data.Favorites.Contains(id) ? "取消收藏" : "收藏";
            };
            search.TextChanged += delegate { RefreshMoves(); };
            filter.SelectedIndexChanged += delegate { RefreshMoves(); };
            filter.SelectedIndex = 0;
        }

        int SelectedMove() { return moves.SelectedIndex < 0 ? -1 : visibleMoves[moves.SelectedIndex]; }

        void RefreshMoves()
        {
            int selected = SelectedMove();
            moves.Items.Clear();
            visibleMoves.Clear();
            IEnumerable<int> ids = filter.SelectedIndex == 2 ? (IEnumerable<int>)data.Recent :
                filter.SelectedIndex == 1 ? (IEnumerable<int>)data.Favorites : new[] { 0,1,2,3,4,5,6 };
            foreach (int i in ids)
            {
                if (!Moves[i].Contains(search.Text.Trim())) continue;
                visibleMoves.Add(i);
                moves.Items.Add((data.Favorites.Contains(i) ? "★  " : "    ") + Moves[i]);
            }
            if (moves.Items.Count > 0) moves.SelectedIndex = Math.Max(0, visibleMoves.IndexOf(selected));
            favoriteMove.Enabled = SelectedMove() >= 0;
        }

        void PlaySelected()
        {
            int id = SelectedMove();
            if (id < 0) return;
            PauseFocus(); PauseStory();
            audition((Pose)id);
            data.Recent.Remove(id); data.Recent.Insert(0, id);
            RefreshMoves();
            Status("点播了「" + Moves[id] + "」。");
            Save();
        }

        void BuildFortunes()
        {
            TableLayoutPanel layout;
            Page("心情签", out layout);
            fortune.Height = 66;
            fortune.Text = "墨爪备了十六张签，来抽一张。";
            Add(layout, fortune, false);
            var buttons = Row();
            Command(buttons, "抽一张", DrawFortune);
            keepFortune = Command(buttons, "收藏这张签", delegate {
                if (currentFortune < 0) return;
                if (!data.KeptFortunes.Remove(currentFortune)) data.KeptFortunes.Insert(0, currentFortune);
                RefreshFortunes(); Save();
            });
            Add(layout, buttons, false);
            Add(layout, new Label { Text = "我的签盒", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, false);
            Add(layout, kept, true);
            kept.SelectedIndexChanged += delegate {
                if (kept.SelectedIndex < 0) return;
                currentFortune = data.KeptFortunes[kept.SelectedIndex];
                ShowFortune();
            };
            RefreshFortunes();
        }
        void DrawFortune()
        {
            PauseFocus();
            var choices = new List<int>();
            for (int i=0; i<Fortunes.Length; i++) if (!data.RecentFortunes.Contains(i)) choices.Add(i);
            if (choices.Count == 0) { data.RecentFortunes.Clear(); DrawFortune(); return; }
            currentFortune = choices[random.Next(choices.Count)];
            data.RecentFortunes.Insert(0, currentFortune);
            while (data.RecentFortunes.Count > 3) data.RecentFortunes.RemoveAt(3);
            audition(Pose.Left);
            kept.ClearSelected();
            ShowFortune(); Save();
        }
        void ShowFortune()
        {
            keepFortune.Enabled = currentFortune >= 0;
            if (currentFortune < 0) return;
            fortune.Text = Fortunes[currentFortune];
            keepFortune.Text = data.KeptFortunes.Contains(currentFortune) ? "移出签盒" : "收藏这张签";
        }
        void RefreshFortunes()
        {
            kept.Items.Clear();
            foreach (int id in data.KeptFortunes) kept.Items.Add(Fortunes[id]);
            ShowFortune();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !shuttingDown)
            {
                shuttingDown = true;
                tick.Stop();
                tick.Dispose();
                PauseFocus();
            }
            base.Dispose(disposing);
        }
    }
}
