using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SootKeys
{
    internal sealed partial class CatToolbox
    {
        static readonly string[] Shows = { "回车帽失踪案", "空格键跳水", "鼠标钓猫", "左右爪交接班", "工位演唱会" };
        static readonly Pose[,] Scripts = {
            {Pose.Enter, Pose.Mouse, Pose.Combo},
            {Pose.Idle, Pose.Space, Pose.Space},
            {Pose.Mouse, Pose.Left, Pose.Mouse},
            {Pose.Left, Pose.Right, Pose.Combo},
            {Pose.Right, Pose.Enter, Pose.Combo}
        };
        static readonly string[,] Captions = {
            {"回车帽不见了。右后脚自告奋勇接受调查。", "墨爪拨开鼠标。底下只有昨天的面包屑。", "一阵乱拍，失踪的键帽落回它的头顶。破案。"},
            {"裁判收起两只前爪，给自己让出跑道。", "左后脚向前一伸，稳稳踩中空格。", "水花零分，空格满分。再跳一次！"},
            {"桌上游过一只白鼠标，尾巴却长在屏幕上。", "墨爪举起左爪，决定遵循就近原则。", "白鼠标上钩了，光标还在逃。"},
            {"左爪递交交班记录：今天只打了半边字。", "右爪认真补齐另一半。", "两只后脚拒绝旁听，直接加入会议。"},
            {"右爪敲出第一拍，观众只有一个光标。", "小猫用回车键打了个底鼓。", "四爪齐奏，第一排观众收到一枚飞来的键帽。"}
        };
        TabPage storyPage, gamePage;
        readonly ComboBox program = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 238 };
        readonly Label subtitle = Paragraph(), focusDisplay = Paragraph();
        readonly NumericUpDown minutes = new NumericUpDown { Minimum = 5, Maximum = 120, Value = 25, Width = 83 };
        Button storyPlay, storyStep, focusPlay, focusStop;
        FlowLayoutPanel focusPresets;
        bool storyRunning, focusing;
        int scene = -1;
        double nextScene, focusEnd, nextSave;

        void BuildStories()
        {
            TableLayoutPanel layout;
            storyPage = Page("小剧场", out layout);
            program.Items.AddRange(Shows);
            var bar = Row();
            bar.Controls.Add(program);
            program.SelectedIndexChanged += delegate { StopStory(); };
            Add(layout, bar, false);
            Add(layout, subtitle, true);
            var buttons = Row();
            storyPlay = Command(buttons, "开演", delegate {
                if (storyRunning) PauseStory();
                else {
                    PauseFocus();
                    storyRunning = true;
                    if (scene < 0) scene = 0;
                    PlayScene();
                }
            });
            storyStep = Command(buttons, "下一幕", delegate { AdvanceScene(); });
            Command(buttons, "结束", StopStory);
            Add(layout, buttons, false);
            program.SelectedIndex = 0;
        }

        void PlayScene()
        {
            int show = program.SelectedIndex;
            audition(Scripts[show, scene]);
            subtitle.Text = string.Format("第 {0} 幕 / 3\n\n{1}", scene+1, Captions[show, scene]);
            nextScene = clock.Elapsed.TotalSeconds + 2.8;
            storyPlay.Text = storyRunning ? "暂停" : "继续";
            storyStep.Enabled = true;
        }
        void AdvanceScene()
        {
            if (scene < 0) return;
            if (++scene < 3) PlayScene();
            else { StopStory(); subtitle.Text = "谢幕。墨爪向唯一的观众鞠了一躬。"; }
        }
        void PauseStory()
        {
            if (!storyRunning) return;
            storyRunning = false;
            storyPlay.Text = "继续";
        }
        void StopStory()
        {
            scene = -1;
            storyRunning = false;
            storyPlay.Text = "开演";
            storyStep.Enabled = false;
            subtitle.Text = "选一出三幕小戏，墨爪会在桌面上配合演出。";
        }

        void BuildFocus()
        {
            TableLayoutPanel layout;
            Page("专注", out layout);
            var presets = Row();
            focusPresets = presets;
            foreach (int n in new[] {15,25,45})
            {
                int dur = n;
                Command(presets, n + " 分钟", delegate { minutes.Value = dur; });
            }
            Add(layout, presets, false);
            var custom = Row();
            custom.Controls.Add(new Label { Text = "时长（分钟）", AutoSize = true, Margin = new Padding(2,6,4,0) });
            custom.Controls.Add(minutes);
            Add(layout, custom, false);
            Add(layout, focusDisplay, true);
            var buttons = Row();
            focusPlay = Command(buttons, "开始专注", delegate {
                if (focusing) { PauseFocus(); return; }
                PausePanelGames();
                if (data.Remaining == 0) data.StartFocus((int)minutes.Value);
                focusEnd = clock.Elapsed.TotalSeconds + data.Remaining;
                nextSave = clock.Elapsed.TotalSeconds + 5;
                focusing = true;
                Status("墨爪替你看钟。关闭百宝箱后仍会继续计时。");
                UpdateFocus(clock.Elapsed.TotalSeconds); Save();
            });
            focusStop = Command(buttons, "提前结束", delegate {
                focusing = false;
                data.Duration = data.Remaining = 0;
                Status("已结束本轮专注。");
                UpdateFocus(clock.Elapsed.TotalSeconds); Save();
            });
            Add(layout, buttons, false);
            UpdateFocus(clock.Elapsed.TotalSeconds);
        }
        void PauseFocus()
        {
            if (!focusing) return;
            UpdateFocus(clock.Elapsed.TotalSeconds);
            focusing = false;
            UpdateFocus(clock.Elapsed.TotalSeconds); Save();
        }
        void UpdateFocus(double now)
        {
            if (focusing)
            {
                data.Remaining = Math.Max(0, (int)Math.Ceiling(focusEnd - now));
                if (data.Remaining == 0)
                {
                    focusing = false;
                    data.FinishFocus(); audition(Pose.Combo);
                    Status("一轮专注完成！墨爪掀起键帽给你庆祝。"); Save();
                }
                else if (now >= nextSave) { nextSave = now + 5; Save(); }
            }
            data.Today();
            string state = data.Duration == 0 ? "设好时长，给自己留一段整块的时间。" :
                string.Format("{0}   {1:D2}:{2:D2}   /   {3} 分钟", focusing ? "专注中" : "已暂停",
                    data.Remaining/60, data.Remaining%60, data.Duration);
            focusDisplay.Text = state + "\n\n今日完成 " + data.CompletedRounds + " 轮，共 " + data.CompletedMinutes + " 分钟。";
            focusStop.Enabled = data.Duration > 0;
            minutes.Enabled = data.Duration == 0;
            focusPresets.Enabled = minutes.Enabled;
            focusPlay.Text = focusing ? "暂停" : data.Remaining > 0 ? "继续专注" : "开始专注";
        }

        readonly Panel arena = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(218,232,222) };
        readonly Button bubble = new Button { Text = "●", Size = new Size(58,58), Visible = false,
            FlatStyle = FlatStyle.Popup, ForeColor = Color.FromArgb(58,87,81),
            BackColor = Color.FromArgb(255,219,143), AccessibleName = "接住泡泡" };
        readonly Label gameInfo = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(2,5,2,5) };
        Button gameStart;
        bool gaming;
        int score, combo;
        double gameRemaining, gameEnd, relocate;
        void BuildGame()
        {
            TableLayoutPanel layout;
            gamePage = Page("接泡泡", out layout);
            arena.Controls.Add(bubble);
            bubble.Click += delegate {
                if (!gaming) return;
                if (clock.Elapsed.TotalSeconds >= gameEnd) { FinishGame(); return; }
                combo++;
                score += 1 + Math.Min(3, (combo-1)/3);
                MoveBubble(); DrawScore();
            };
            arena.MouseDown += delegate { if (gaming) {combo = 0; DrawScore();} };
            Add(layout, gameInfo, false);
            Add(layout, arena, true);
            var buttons = Row();
            gameStart = Command(buttons, "开始 30 秒", delegate {
                if (gaming) { PauseGame(); return; }
                PauseFocus();
                if (gameRemaining <= 0) { score = combo = 0; gameRemaining = 30; }
                gaming = true; gameEnd = clock.Elapsed.TotalSeconds + gameRemaining;
                gameStart.Text = "暂停"; bubble.Visible = true;
                MoveBubble(); bubble.Focus(); DrawScore();
                Status("点泡泡，或聚焦后按空格。连续接住可加分。");
            });
            Add(layout, buttons, false);
            DrawScore();
        }
        void MoveBubble()
        {
            bubble.Location = new Point(random.Next(Math.Max(1, arena.ClientSize.Width-bubble.Width)),
                random.Next(Math.Max(1, arena.ClientSize.Height-bubble.Height)));
            relocate = clock.Elapsed.TotalSeconds + 1.4;
        }
        void DrawScore()
        {
            gameInfo.Text = string.Format("剩余 {0:00} 秒    得分 {1}    连击 {2}    今日最佳 {3}",
                Math.Ceiling(gameRemaining), score, combo, data.Best);
        }
        void PauseGame()
        {
            if (!gaming) return;
            gameRemaining = Math.Max(0, gameEnd-clock.Elapsed.TotalSeconds);
            if (gameRemaining == 0) { FinishGame(); return; }
            gaming = false; bubble.Visible = false; gameStart.Text = "继续游戏"; DrawScore();
        }
        void FinishGame()
        {
            gaming = false; gameRemaining = 0; bubble.Visible = false;
            data.Today(); data.Best = Math.Max(data.Best, score);
            gameStart.Text = "再来一局"; DrawScore();
            Status("收摊。墨爪数到了 " + score + " 分。"); Save();
        }
        void Pulse()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (focusing || (Visible && tabs.SelectedIndex == 2)) UpdateFocus(now);
            if (storyRunning && now >= nextScene) AdvanceScene();
            if (!gaming) return;
            gameRemaining = Math.Max(0, gameEnd - now);
            if (gameRemaining == 0) { FinishGame(); return; }
            if (now >= relocate) { combo = 0; MoveBubble(); }
            DrawScore();
        }

        internal static void SelfTest()
        {
            string path = Path.Combine(Path.GetTempPath(), "SootKeys-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var actions = new List<Pose>();
                using (var box = new CatToolbox(actions.Add, path))
                {
                    VerifySayings(box.sayings);
                    box.moves.SelectedIndex = 6;
                    box.PlaySelected();
                    if (actions.Count != 1 || actions[0] != Pose.Combo) throw new Exception("Action audition failed");
                    box.data.Favorites.Add(6);
                    box.filter.SelectedIndex = 1;
                    if (box.moves.Items.Count != 1) throw new Exception("Favorite filter failed");
                    for (int i=0; i<40; i++)
                    {
                        var old = new List<int>(box.data.RecentFortunes);
                        box.DrawFortune();
                        if (old.Contains(box.currentFortune)) throw new Exception("Fortune repeated");
                    }
                    box.data.KeptFortunes.Add(box.currentFortune);
                    box.data.StartFocus(5); box.data.Remaining = 142; box.Save();
                    box.program.SelectedIndex = 0;
                    box.scene = 0; box.storyRunning = true; box.PlayScene();
                    box.AdvanceScene(); box.PauseStory();
                    if (box.scene != 1 || box.storyRunning) throw new Exception("Story pause failed");
                    box.score = 7; box.FinishGame();
                }
                using (var restored = new CatToolbox(actions.Add, path))
                {
                    if (restored.focusing || restored.data.Remaining != 142 || restored.data.Best != 7 ||
                        !restored.data.Favorites.Contains(6) || restored.data.Recent[0] != 6 ||
                        restored.data.KeptFortunes.Count != 1) throw new Exception("Toolbox recovery failed");
                    restored.focusing = true;
                    restored.focusEnd = restored.clock.Elapsed.TotalSeconds;
                    restored.UpdateFocus(restored.focusEnd+1);
                    if (restored.data.CompletedMinutes != 5 || restored.data.CompletedRounds != 1 ||
                        restored.data.Remaining != 0) throw new Exception("Focus completion failed");
                }
            }
            finally
            {
                foreach (string file in new[] { "toolbox.xml", "toolbox.xml.tmp" }) File.Delete(Path.Combine(path,file));
                if (Directory.Exists(path)) Directory.Delete(path);
            }
        }

        static void VerifySayings(CatQuips sayings)
        {
            string previous=null;
            for (int cycle=0; cycle<3; cycle++)
            {
                var seen=new HashSet<string>(StringComparer.Ordinal);
                for (int i=0; i<sayings.Count; i++)
                {
                    string line=sayings.Next();
                    if (line==previous || !seen.Add(line)) throw new Exception("Quip repeated.");
                    previous=line;
                }
                if (seen.Count<50) throw new Exception("Too few distinct quips.");
            }
            var desk=new Rectangle(-1600,0,1600,900);
            var size=new Size(290,100);
            foreach (int x in new[] {desk.Left,desk.Right-110})
            foreach (int y in new[] {desk.Top,desk.Bottom-120})
            {
                bool above; int tip;
                var cat=new Rectangle(x,y,110,120);
                Point at=CatSpeechBubble.Place(cat,desk,size,7,24,out above,out tip);
                if (!desk.Contains(new Rectangle(at,size)) || above!=(y>desk.Top))
                    throw new Exception("Speech bubble exceeded monitor bounds.");
            }
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point position);
        internal void VerifyPopup(Form actor)
        {
            IntPtr foreground=GetForegroundWindow();
            Quip(actor);
            Point center=new Point(speech.Left+speech.Width/2,speech.Top+speech.Height/2);
            if (Visible || !speech.IsShowing || GetForegroundWindow()!=foreground ||
                WindowFromPoint(center)==speech.Handle || !Screen.FromControl(actor).WorkingArea.Contains(speech.Bounds))
                throw new Exception("Speech must stay near the cat without opening the toolbox or intercepting input.");
            speech.UpdateBubble(8000);
            if (Visible || speech.IsShowing) throw new Exception("Speech did not expire.");
        }
    }
}
