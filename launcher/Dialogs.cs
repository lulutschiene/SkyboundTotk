// The launcher's dialogs: settings, save backups, mods and updates (Launcher.cs opens them).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

static class Ui
{
    public static Form Dialog(string title, int w, int h)
    {
        return new Form { Text = title, ClientSize = new Size(w, h), BackColor = Theme.Back, ForeColor = Theme.Text,
                          Font = new Font("Segoe UI", 10f), FormBorderStyle = FormBorderStyle.FixedDialog,
                          StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
                          ShowInTaskbar = false, ShowIcon = false };
    }

    public static Button Button(string text, bool primary)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 34), FlatStyle = FlatStyle.Flat,
                             BackColor = primary ? Theme.Gold : Theme.Panel,
                             ForeColor = primary ? Color.FromArgb(0x3A, 0x26, 0x05) : Theme.Text,
                             Margin = new Padding(8, 0, 0, 0) };
        b.FlatAppearance.BorderColor = primary ? Theme.Gold : Theme.Line;
        b.EnabledChanged += delegate {
            b.BackColor = !b.Enabled ? Theme.Line : primary ? Theme.Gold : Theme.Panel;
            b.FlatAppearance.BorderColor = !b.Enabled ? Theme.Line : primary ? Theme.Gold : Theme.Line;
        };
        return b;
    }

    public static Label Label(string text, bool muted = false)
    {
        return new Label { Text = text, AutoSize = true, ForeColor = muted ? Theme.Muted : Theme.Text,
                           Margin = new Padding(0, 8, 12, 0) };
    }

    public static ComboBox Combo(IEnumerable<string> items, int width)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Theme.Panel,
                               ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 0, 4) };
        c.Items.AddRange(items.Cast<object>().ToArray());
        return c;
    }

    public static CheckBox Check(string text, bool on)
    {
        return new CheckBox { Text = text, Checked = on, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 6) };
    }

    // A row of buttons along the bottom right
    public static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 54,
                                        Padding = new Padding(12, 10, 12, 10), BackColor = Theme.Back };
        row.Controls.AddRange(buttons);
        return row;
    }

    public static ListView List()
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                                  HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Theme.Panel,
                                  ForeColor = Theme.Text, HeaderStyle = ColumnHeaderStyle.Nonclickable, OwnerDraw = true };
        // (a dark header; the rows draw as usual, and the last column reaches the right edge)
        list.DrawColumnHeader += (s, e) => {
            using (var b = new SolidBrush(Theme.Back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        list.DrawItem += (s, e) => { e.DrawDefault = true; };
        list.DrawSubItem += (s, e) => { e.DrawDefault = true; };
        list.Resize += delegate {
            if (list.Columns.Count == 0) return;
            int rest = list.ClientSize.Width;
            for (int i = 0; i < list.Columns.Count - 1; i++) rest -= list.Columns[i].Width;
            list.Columns[list.Columns.Count - 1].Width = Math.Max(120, rest);
        };
        return list;
    }

    public static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // (to the Recycle Bin, so a removal can still be undone)
    public static void Recycle(string dir)
    {
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}

// ---- settings: what the game reads at start (HOS_* variables, a64-kit/hos) and the launcher's own behaviour ----
static class GameSettings
{
    public static readonly string[] Sizes = { "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160", "2560x1080",
                                              "3440x1440" };
    public static readonly string[] Caps = { "Display refresh rate", "30", "60", "120", "144" };
    public static readonly string[] Languages = { "en-US", "en-GB", "fr", "fr-CA", "de", "it", "es", "es-419", "nl", "ru",
                                                  "ja", "ko", "zh-CN", "zh-TW" };

    // The variables the game gets from the launcher's settings
    public static void Apply(Settings s, ProcessStartInfo start)
    {
        start.EnvironmentVariables["HOS_WINDOW_SIZE"] = s.Get("window_size", "1280x720");
        if (s.Get("fullscreen", "0") == "1") start.EnvironmentVariables["HOS_FULLSCREEN"] = "1";
        if (s.Get("fps_cap", "") != "") start.EnvironmentVariables["HOS_FPS_CAP"] = s.Get("fps_cap", "");
        start.EnvironmentVariables["HOS_LANGUAGE"] = s.Get("language", "en-US");
        if (s.Get("invert_y", "0") == "1") start.EnvironmentVariables["HOS_MOUSE_INVERT_Y"] = "1";
        if (s.Get("audio", "1") == "0") start.EnvironmentVariables["HOS_AUDIO"] = "0";
    }

    public static bool Show(IWin32Window owner, Settings s, List<GameVersion> versions, string[] modeLabels, int mode,
                            Action<int> setMode)
    {
        using (var f = Ui.Dialog("Settings", 600, 640))
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(20, 16, 20, 0),
                                              AutoScroll = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<string> heading = text => {
                var title = Ui.Label(text);
                title.ForeColor = Theme.Gold;
                title.Margin = new Padding(0, 14, 0, 4);
                grid.Controls.Add(title);
                grid.SetColumnSpan(title, 2);
            };
            Action<string, Control> row = (text, c) => { grid.Controls.Add(Ui.Label(text, true)); grid.Controls.Add(c); };

            heading("Display");
            var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200, BackColor = Theme.Panel,
                                      ForeColor = Theme.Text, Margin = new Padding(0, 4, 0, 4) };
            size.Items.AddRange(Sizes);
            size.Text = s.Get("window_size", "1280x720");
            row("Window size", size);
            var full = Ui.Check("Start in fullscreen (F11 switches in the game)", s.Get("fullscreen", "0") == "1");
            grid.Controls.Add(new Label());
            grid.Controls.Add(full);
            var cap = Ui.Combo(Caps, 200);
            cap.SelectedIndex = Math.Max(0, Array.IndexOf(Caps, s.Get("fps_cap", "")));
            row("Frame rate cap", cap);

            heading("Game");
            var lang = Ui.Combo(Languages, 200);
            lang.SelectedIndex = Math.Max(0, Array.IndexOf(Languages, s.Get("language", "en-US")));
            row("Language", lang);
            var audio = Ui.Check("Sound", s.Get("audio", "1") == "1");
            grid.Controls.Add(new Label());
            grid.Controls.Add(audio);

            heading("Input");
            var input = Ui.Combo(modeLabels, 320);
            input.SelectedIndex = mode;
            row("Input mode", input);
            var sens = new NumericUpDown { Minimum = 0.05m, Maximum = 20m, DecimalPlaces = 2, Increment = 0.1m, Width = 90,
                                           BackColor = Theme.Panel, ForeColor = Theme.Text, Margin = new Padding(0, 4, 0, 4) };
            sens.Value = (decimal)ReadSens(versions);
            row("Mouse sensitivity", sens);
            var invert = Ui.Check("Invert mouse up and down", s.Get("invert_y", "0") == "1");
            grid.Controls.Add(new Label());
            grid.Controls.Add(invert);

            heading("Launcher");
            var hide = Ui.Check("Minimise the launcher while playing", s.Get("hide_while_playing", "1") == "1");
            grid.Controls.Add(new Label());
            grid.Controls.Add(hide);
            var backup = Ui.Check("Back up saves every time the game starts (keeps the last 10)", s.Get("backup_on_start", "1") == "1");
            grid.Controls.Add(new Label());
            grid.Controls.Add(backup);

            var save = Ui.Button("Save", true);
            var cancel = Ui.Button("Cancel", false);
            save.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            f.Controls.Add(grid);
            f.Controls.Add(Ui.Buttons(save, cancel));
            f.AcceptButton = save;
            f.CancelButton = cancel;
            if (f.ShowDialog(owner) != DialogResult.OK) return false;

            int w, h;
            var parts = size.Text.Trim().ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w >= 640 && h >= 360)
                s.Set("window_size", w + "x" + h);
            s.Set("fullscreen", full.Checked ? "1" : "0");
            s.Set("fps_cap", cap.SelectedIndex == 0 ? "" : Caps[cap.SelectedIndex]);
            s.Set("language", Languages[lang.SelectedIndex]);
            s.Set("audio", audio.Checked ? "1" : "0");
            s.Set("invert_y", invert.Checked ? "1" : "0");
            s.Set("hide_while_playing", hide.Checked ? "1" : "0");
            s.Set("backup_on_start", backup.Checked ? "1" : "0");
            setMode(input.SelectedIndex);
            // (the game keeps the sensitivity in each version's user\mouse_sens.txt; - and = in the game change it too)
            foreach (var v in versions)
            {
                Directory.CreateDirectory(v.User);
                File.WriteAllText(Path.Combine(v.User, "mouse_sens.txt"),
                                  ((double)sens.Value).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "\n");
            }
            return true;
        }
    }

    static double ReadSens(List<GameVersion> versions)
    {
        foreach (var v in versions)
        {
            var p = Path.Combine(v.User, "mouse_sens.txt");
            double d;
            if (File.Exists(p) && double.TryParse(File.ReadAllText(p).Trim(), System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture, out d))
                return Math.Max(0.05, Math.Min(20, d));
        }
        return 1.0;
    }
}

// ---- save backups: user\save-backups\<kind>-<yyyyMMdd-HHmmss>, kinds auto (each start), import, restore ----
static class Backups
{
    const int KeepAuto = 10;

    public static string Dir(GameVersion v) { return Path.Combine(v.User, "save-backups"); }

    public static void Make(GameVersion v, string kind)
    {
        if (!Directory.Exists(v.Saves) || !Directory.EnumerateFileSystemEntries(v.Saves).Any()) return;
        Ui.CopyDir(v.Saves, Path.Combine(Dir(v), kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        if (kind != "auto") return;
        var old = Directory.GetDirectories(Dir(v), "auto-*").OrderByDescending(d => d).Skip(KeepAuto).ToList();
        foreach (var d in old) Ui.Recycle(d);
    }

    public static void Show(IWin32Window owner, GameVersion v, Action changed)
    {
        using (var f = Ui.Dialog("Save backups, version " + v.Name, 620, 420))
        {
            var list = Ui.List();
            list.Columns.Add("Made", 190);
            list.Columns.Add("Why", 150);
            list.Columns.Add("Slots", 80);
            list.Columns.Add("Folder", 180);
            Action fill = () => {
                list.Items.Clear();
                if (!Directory.Exists(Dir(v))) return;
                foreach (var d in Directory.GetDirectories(Dir(v)).OrderByDescending(d => Directory.GetCreationTime(d)))
                {
                    string name = Path.GetFileName(d), kind = name.Split('-')[0];
                    string why = kind == "auto" ? "game started" : kind == "import" ? "before an import" :
                                 kind == "restore" ? "before a restore" : name;
                    var item = new ListViewItem(Directory.GetCreationTime(d).ToString("g")) { Tag = d };
                    item.SubItems.Add(why);
                    item.SubItems.Add(Directory.GetDirectories(d, "slot_*").Length.ToString());
                    item.SubItems.Add(name);
                    list.Items.Add(item);
                }
            };
            fill();
            var restore = Ui.Button("Restore", true);
            var open = Ui.Button("Open folder", false);
            var close = Ui.Button("Close", false);
            close.DialogResult = DialogResult.Cancel;
            restore.Click += delegate {
                if (list.SelectedItems.Count == 0) return;
                var d = (string)list.SelectedItems[0].Tag;
                if (MessageBox.Show(f, "Replace the current saves of " + v.Name + " with this backup?\n\nThe current saves " +
                                    "are kept as a backup first.", "Restore saves", MessageBoxButtons.OKCancel) != DialogResult.OK)
                    return;
                // (the current saves move into the backups as they are, then the backup is copied in: nothing is lost)
                if (Directory.Exists(v.Saves))
                {
                    Directory.CreateDirectory(Dir(v));
                    Directory.Move(v.Saves, Path.Combine(Dir(v), "restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                }
                Ui.CopyDir(d, v.Saves);
                fill();
                changed();
                MessageBox.Show(f, "Restored. Use Load on the title screen to pick a slot.", "Restore saves");
            };
            open.Click += delegate { Directory.CreateDirectory(Dir(v)); Process.Start("explorer.exe", "\"" + Dir(v) + "\""); };
            f.Controls.Add(list);
            f.Controls.Add(Ui.Buttons(close, open, restore));
            f.CancelButton = close;
            f.ShowDialog(owner);
        }
    }
}

// ---- mods: versions\<v>\mods, in the emulators' layouts (a64-kit/hos/mods.c); a '-' in front of a name turns it off ----
static class Mods
{
    class Mod { public string Path, Name, Kind; public bool On; }

    static string Root(GameVersion v) { return System.IO.Path.Combine(v.Dir, "mods"); }

    static bool IsMod(string d)
    {
        return Directory.Exists(System.IO.Path.Combine(d, "romfs")) || Directory.Exists(System.IO.Path.Combine(d, "exefs"));
    }

    static void Find(string dir, List<Mod> into)
    {
        foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x))
        {
            string name = System.IO.Path.GetFileName(d);
            if (IsMod(d))
            {
                bool rom = Directory.Exists(System.IO.Path.Combine(d, "romfs")), exe = Directory.Exists(System.IO.Path.Combine(d, "exefs"));
                into.Add(new Mod { Path = d, Name = name.TrimStart('-', '_'), On = !name.StartsWith("-") && !name.StartsWith("_"),
                                   Kind = rom && exe ? "RomFS + ExeFS" : rom ? "RomFS" : "ExeFS" });
            }
            else Find(d, into);
        }
    }

    public static void Show(IWin32Window owner, GameVersion v)
    {
        Directory.CreateDirectory(Root(v));
        using (var f = Ui.Dialog("Mods, version " + v.Name, 680, 440))
        {
            var list = Ui.List();
            list.CheckBoxes = true;
            list.Columns.Add("Mod", 300);
            list.Columns.Add("Type", 130);
            list.Columns.Add("Folder", 220);
            var mods = new List<Mod>();
            bool filling = false;
            Action fill = () => {
                filling = true;
                mods.Clear();
                Find(Root(v), mods);
                list.Items.Clear();
                foreach (var m in mods)
                {
                    var item = new ListViewItem(m.Name) { Checked = m.On, Tag = m };
                    item.SubItems.Add(m.Kind);
                    item.SubItems.Add(m.Path.Substring(Root(v).Length).TrimStart('\\'));
                    list.Items.Add(item);
                }
                filling = false;
            };
            list.ItemChecked += (s, e) => {
                if (filling) return;
                var m = (Mod)e.Item.Tag;
                string parent = System.IO.Path.GetDirectoryName(m.Path), bare = System.IO.Path.GetFileName(m.Path).TrimStart('-', '_');
                string to = System.IO.Path.Combine(parent, (e.Item.Checked ? "" : "-") + bare);
                if (to == m.Path) return;
                try { Directory.Move(m.Path, to); m.Path = to; m.On = e.Item.Checked; }
                catch (Exception x) { MessageBox.Show(f, "Could not rename the mod folder: " + x.Message, "Mods"); }
                e.Item.SubItems[2].Text = m.Path.Substring(Root(v).Length).TrimStart('\\');
            };
            fill();
            var note = new Label { Dock = DockStyle.Top, Height = 46, ForeColor = Theme.Muted, Padding = new Padding(12, 8, 12, 0),
                                   Text = "Ticked mods load the next time the game starts; mods lower in the list win when two " +
                                          "change the same file. ExeFS code patches only apply after a rebuild." };
            var addZip = Ui.Button("Add from .zip...", true);
            var addDir = Ui.Button("Add folder...", false);
            var remove = Ui.Button("Remove", false);
            var open = Ui.Button("Open folder", false);
            var close = Ui.Button("Close", false);
            close.DialogResult = DialogResult.Cancel;
            addZip.Click += delegate {
                using (var d = new OpenFileDialog { Filter = "Mod archives (*.zip)|*.zip", Title = "Add a mod" })
                {
                    if (d.ShowDialog(f) != DialogResult.OK) return;
                    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skybound-mod-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(d.FileName, tmp);
                        AddFrom(f, tmp, System.IO.Path.GetFileNameWithoutExtension(d.FileName), Root(v));
                    }
                    catch (Exception x) { MessageBox.Show(f, "Could not add the mod: " + x.Message, "Mods"); }
                    finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
                    fill();
                }
            };
            addDir.Click += delegate {
                using (var d = new FolderBrowserDialog { Description = "Pick the mod's folder (the one with romfs or exefs in it)" })
                {
                    if (d.ShowDialog(f) != DialogResult.OK) return;
                    try { AddFrom(f, d.SelectedPath, System.IO.Path.GetFileName(d.SelectedPath), Root(v)); }
                    catch (Exception x) { MessageBox.Show(f, "Could not add the mod: " + x.Message, "Mods"); }
                    fill();
                }
            };
            remove.Click += delegate {
                if (list.SelectedItems.Count == 0) return;
                var m = (Mod)list.SelectedItems[0].Tag;
                if (MessageBox.Show(f, "Move " + m.Name + " to the Recycle Bin?", "Remove mod", MessageBoxButtons.OKCancel) !=
                    DialogResult.OK) return;
                Ui.Recycle(m.Path);
                fill();
            };
            open.Click += delegate { Process.Start("explorer.exe", "\"" + Root(v) + "\""); };
            f.Controls.Add(list);
            f.Controls.Add(note);
            f.Controls.Add(Ui.Buttons(close, open, remove, addDir, addZip));
            f.CancelButton = close;
            f.ShowDialog(owner);
        }
    }

    // Copies every mod folder found in source (or source itself when it is one) into mods\
    static void AddFrom(IWin32Window owner, string source, string fallbackName, string root)
    {
        var found = new List<string>();
        if (IsMod(source)) found.Add(source);
        else
        {
            var stack = new Stack<string>(Directory.GetDirectories(source));
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (IsMod(d)) found.Add(d);
                else foreach (var c in Directory.GetDirectories(d)) stack.Push(c);
            }
        }
        if (found.Count == 0)
        {
            MessageBox.Show(owner, "No romfs or exefs folder in there, so it is not a mod this game can load.", "Mods");
            return;
        }
        foreach (var d in found)
        {
            string name = d == source ? fallbackName : System.IO.Path.GetFileName(d);
            string to = System.IO.Path.Combine(root, name);
            for (int i = 2; Directory.Exists(to) || Directory.Exists(System.IO.Path.Combine(root, "-" + name)); i++)
                to = System.IO.Path.Combine(root, name + " (" + i + ")");
            Ui.CopyDir(d, to);
        }
    }
}

// ---- updates: git in the source folder, then launcher\update.py rebuilds every installed version ----
static class Updates
{
    public static string Repo(string launcherDir)
    {
        // (dist\Skybound\ inside the source folder)
        var d = new DirectoryInfo(launcherDir.TrimEnd('\\'));
        for (int i = 0; i < 3 && d != null; i++, d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "build_game.py"))) return d.FullName;
        return null;
    }

    static string Git(string repo, string args)
    {
        var p = Process.Start(new ProcessStartInfo("git", "-C \"" + repo + "\" " + args) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8 });
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Trim();
    }

    public static void Show(IWin32Window owner, string launcherDir, Func<bool> gameRunning)
    {
        string repo = Repo(launcherDir);
        if (repo == null)
        {
            MessageBox.Show(owner, "The SkyboundTOTK source folder was not found above " + launcherDir + ".", "Updates");
            return;
        }
        using (var f = Ui.Dialog("Updates", 760, 520))
        {
            f.FormBorderStyle = FormBorderStyle.Sizable;
            var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                                    BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
                                    Font = new Font("Consolas", 9.5f) };
            var check = Ui.Button("Check for updates", false);
            var update = Ui.Button("Update and rebuild", true);
            var close = Ui.Button("Close", false);
            update.Enabled = false;
            Process running = null;
            Action<string> say = line => log.AppendText(line + Environment.NewLine);
            check.Click += delegate {
                log.Clear();
                say("Source: " + repo);
                say("Installed: " + Git(repo, "log -1 --format=\"%h %cd  %s\" --date=short"));
                say("Checking GitHub...");
                Application.DoEvents();
                Git(repo, "fetch --quiet origin");
                // (the original project's main branch: Skybound's own changes are commits on top of it)
                string news = Git(repo, "log --format=\"%h %cd  %s\" --date=short HEAD..origin/main");
                if (news == "")
                {
                    say("You have the newest version.");
                    update.Enabled = false;
                    return;
                }
                say("");
                say(news.Split('\n').Length + " new change(s):");
                say(news.Replace("\n", Environment.NewLine));
                say("");
                say("Update and rebuild takes about 20 minutes per installed version. Saves, mods and settings stay.");
                update.Enabled = true;
            };
            update.Click += delegate {
                if (gameRunning())
                {
                    MessageBox.Show(f, "Close the game first.", "Updates");
                    return;
                }
                update.Enabled = check.Enabled = false;
                say("");
                var start = new ProcessStartInfo("python", "-u \"" + Path.Combine(repo, "launcher", "update.py") + "\"") {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, WorkingDirectory = repo };
                try { running = Process.Start(start); }
                catch (Exception x) { say("Could not start Python: " + x.Message); check.Enabled = true; return; }
                DataReceivedEventHandler pipe = (s, e) => {
                    if (e.Data != null) f.BeginInvoke((Action)delegate { say(e.Data); });
                };
                running.OutputDataReceived += pipe;
                running.ErrorDataReceived += pipe;
                running.EnableRaisingEvents = true;
                running.Exited += delegate {
                    f.BeginInvoke((Action)delegate {
                        say(running.ExitCode == 0 ? "Done. Restart the launcher to use the new version."
                                                  : "The update stopped (exit code " + running.ExitCode + "). The previous build is still installed.");
                        check.Enabled = true;
                    });
                };
                running.BeginOutputReadLine();
                running.BeginErrorReadLine();
            };
            f.FormClosing += (s, e) => {
                if (running != null && !running.HasExited &&
                    MessageBox.Show(f, "The update is still running. Close this window anyway? It keeps running in the " +
                                    "background.", "Updates", MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
            };
            close.Click += delegate { f.Close(); };
            f.Controls.Add(log);
            f.Controls.Add(Ui.Buttons(close, update, check));
            f.Shown += delegate { check.PerformClick(); };
            f.ShowDialog(owner);
        }
    }
}
