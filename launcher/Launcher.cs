// Skybound launcher: a game list in the style of Ryujinx and suyu for the versions installed under versions\<v>\
// (build_game.py --version <v>), with the input mode, time played and save import (suyu, or any folder of slots).
// C# 5 for the compiler that ships with Windows (build.bat).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Back = Color.FromArgb(0x1E, 0x1F, 0x22), Panel = Color.FromArgb(0x2B, 0x2D, 0x31),
        Line = Color.FromArgb(0x3A, 0x3C, 0x42), Text = Color.FromArgb(0xE6, 0xE6, 0xE6),
        Muted = Color.FromArgb(0x9A, 0xA0, 0xA6), Gold = Color.FromArgb(0xE8, 0xB0, 0x4B),
        Sky = Color.FromArgb(0x1F, 0x6F, 0x8B), Select = Color.FromArgb(0x24, 0x4F, 0x60);
}

class DarkColours : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin { get { return Theme.Panel; } }
    public override Color MenuStripGradientEnd { get { return Theme.Panel; } }
    public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
    public override Color MenuItemSelected { get { return Theme.Select; } }
    public override Color MenuItemSelectedGradientBegin { get { return Theme.Select; } }
    public override Color MenuItemSelectedGradientEnd { get { return Theme.Select; } }
    public override Color MenuItemPressedGradientBegin { get { return Theme.Select; } }
    public override Color MenuItemPressedGradientEnd { get { return Theme.Select; } }
    public override Color MenuItemBorder { get { return Theme.Sky; } }
    public override Color MenuBorder { get { return Theme.Line; } }
    public override Color SeparatorDark { get { return Theme.Line; } }
    public override Color SeparatorLight { get { return Theme.Line; } }
    public override Color CheckBackground { get { return Theme.Sky; } }
    public override Color CheckSelectedBackground { get { return Theme.Sky; } }
    public override Color CheckPressedBackground { get { return Theme.Sky; } }
}

// The launcher's own settings, key=value lines in launcher.ini beside it
class Settings
{
    readonly string path;
    readonly Dictionary<string, string> values = new Dictionary<string, string>();

    public Settings(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
    }

    public string Get(string key, string fallback)
    {
        string v;
        return values.TryGetValue(key, out v) ? v : fallback;
    }

    public void Set(string key, string value)
    {
        values[key] = value;
        File.WriteAllLines(path, values.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value).ToArray());
    }
}

class GameVersion
{
    public string Name, Dir;
    public string Exe { get { return Path.Combine(Dir, "SkyboundTOTK.exe"); } }
    public string User { get { return Path.Combine(Dir, "user"); } }
    // (the one account the game's OS layer reports: a64-kit/hos/nn_applet.c)
    public string Saves { get { return Path.Combine(User, "save", "5a3c9e1f0b7d24680000000000000001"); } }
}

class LauncherForm : Form
{
    static readonly string[] Modes = { "both", "controller", "keyboard" };
    static readonly string[] ModeLabels = { "Controller and keyboard", "Controller only (works in the background)",
                                            "Keyboard and mouse only" };
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly Settings settings;
    readonly ListView list = new ListView();
    readonly ComboBox mode = new ComboBox();
    readonly Button play = new Button();
    readonly Label status = new Label();
    readonly ToolStripMenuItem[] modeItems = new ToolStripMenuItem[3];
    List<GameVersion> versions = new List<GameVersion>();
    Process running;

    public LauncherForm()
    {
        settings = new Settings(Path.Combine(root, "launcher.ini"));
        Text = "Skybound";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(980, 560);
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(BuildList());
        Controls.Add(BuildBottom());
        Controls.Add(BuildHeader());
        Controls.Add(BuildMenu());
        SetMode(Array.IndexOf(Modes, settings.Get("input_mode", "both")));
        Shown += delegate { Reload(); };
        try { StartMenuShortcut(); } catch (Exception) { }
    }

    // Start menu\Programs\Skybound.lnk, so Windows search finds the launcher; rewritten when the folder has moved
    static void StartMenuShortcut()
    {
        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Skybound.lnk");
        string exe = Application.ExecutablePath;
        Type type = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(type);
        object link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell,
                                        new object[] { lnk });
        Type lt = link.GetType();
        string now = (string)lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null);
        if (File.Exists(lnk) && string.Equals(now, exe, StringComparison.OrdinalIgnoreCase)) return;
        lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { exe });
        lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link,
                        new object[] { Path.GetDirectoryName(exe) });
        lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { exe + ",0" });
        lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link,
                        new object[] { "Skybound: Tears of the Kingdom launcher" });
        lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
    }

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColours()), BackColor = Theme.Panel,
                                   ForeColor = Theme.Text };
        var file = Item("File");
        file.DropDownItems.Add(Item("Open Skybound folder", delegate { Explore(root); }));
        file.DropDownItems.Add(Item("Refresh game list", delegate { Reload(); }));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Exit", delegate { Close(); }));
        var options = Item("Options");
        var input = Item("Input mode");
        for (int i = 0; i < 3; i++)
        {
            int m = i;
            modeItems[i] = Item(ModeLabels[i], delegate { SetMode(m); });
            input.DropDownItems.Add(modeItems[i]);
        }
        options.DropDownItems.Add(input);
        options.DropDownItems.Add(Item("Settings...", delegate { ShowSettings(); }));
        var tools = Item("Tools");
        tools.DropDownItems.Add(Item("Mods...", delegate { var v = Selected(); if (v != null) Mods.Show(this, v); }));
        tools.DropDownItems.Add(Item("Save backups...", delegate { var v = Selected(); if (v != null) Backups.Show(this, v, Reload); }));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Item("Import saves from suyu", delegate { ImportSuyu(); }));
        tools.DropDownItems.Add(Item("Import saves from a folder...", delegate { ImportFolder(); }));
        tools.DropDownItems.Add(Item("Copy saves from another version...", delegate { CopyFromVersion(); }));
        var help = Item("Help");
        help.DropDownItems.Add(Item("Check for updates...", delegate { Updates.Show(this, root, GameRunning); }));
        help.DropDownItems.Add(Item("Controls", delegate { ShowControls(); }));
        help.DropDownItems.Add(Item("About Skybound", delegate {
            MessageBox.Show(this, "Skybound launcher for SkyboundTOTK, a native PC build of your own Tears of the " +
                            "Kingdom dump.\n\nNot affiliated with Nintendo.", "About Skybound");
        }));
        menu.Items.AddRange(new ToolStripItem[] { file, options, tools, help });
        foreach (ToolStripMenuItem top in menu.Items) Darken(top);
        return menu;
    }

    static void Darken(ToolStripMenuItem item)
    {
        item.ForeColor = Theme.Text;
        foreach (ToolStripItem child in item.DropDownItems)
        {
            child.ForeColor = Theme.Text;
            child.BackColor = Theme.Panel;
            var sub = child as ToolStripMenuItem;
            if (sub != null) Darken(sub);
        }
    }

    static ToolStripMenuItem Item(string text, EventHandler click = null)
    {
        var item = new ToolStripMenuItem(text);
        if (click != null) item.Click += click;
        return item;
    }

    Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Back, Padding = new Padding(18, 12, 18, 8) };
        var logo = new PictureBox { Image = new Icon(Icon, 64, 64).ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
                                    Size = new Size(60, 60), Location = new Point(18, 12) };
        var title = new Label { Text = "Skybound", Font = new Font("Georgia", 22f), ForeColor = Theme.Gold, AutoSize = true,
                                Location = new Point(88, 10) };
        var sub = new Label { Text = "Tears of the Kingdom, native on PC", ForeColor = Theme.Muted, AutoSize = true,
                              Location = new Point(92, 50) };
        header.Controls.AddRange(new Control[] { logo, title, sub });
        return header;
    }

    Control BuildList()
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.BorderStyle = BorderStyle.None;
        list.BackColor = Theme.Panel;
        list.ForeColor = Theme.Text;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.SmallImageList = new ImageList { ImageSize = new Size(48, 48), ColorDepth = ColorDepth.Depth32Bit };
        list.SmallImageList.Images.Add(new Icon(Icon, 48, 48).ToBitmap());
        list.Columns.Add("Game", 380);
        list.Columns.Add("Version", 90);
        list.Columns.Add("Time played", 120);
        list.Columns.Add("Last played", 170);
        list.Columns.Add("Saves", 220);
        // (the last column reaches the right edge, so no unpainted header is left past it)
        list.Resize += delegate {
            int rest = list.ClientSize.Width;
            for (int i = 0; i < list.Columns.Count - 1; i++) rest -= list.Columns[i].Width;
            list.Columns[list.Columns.Count - 1].Width = Math.Max(160, rest);
        };
        list.OwnerDraw = true;
        list.DrawColumnHeader += (s, e) => {
            using (var b = new SolidBrush(Theme.Back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        list.DrawSubItem += (s, e) => {
            bool sel = e.Item.Selected;
            using (var b = new SolidBrush(sel ? Theme.Select : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = e.Bounds;
            if (e.ColumnIndex == 0)
            {
                e.Graphics.DrawImage(list.SmallImageList.Images[0], r.Left + 8, r.Top + (r.Height - 48) / 2, 48, 48);
                r = new Rectangle(r.Left + 66, r.Top, r.Width - 66, r.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, new Font(Font, FontStyle.Bold), r, Theme.Text,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                return;
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, Rectangle.Inflate(r, -6, 0),
                                  e.ColumnIndex == 1 ? Theme.Gold : Theme.Text,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        list.DoubleClick += delegate { Play(); };
        list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Play(); };
        list.SelectedIndexChanged += delegate { UpdateButtons(); };

        var context = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColours()) };
        context.Items.Add(Item("Play", delegate { Play(); }));
        context.Items.Add(new ToolStripSeparator());
        context.Items.Add(Item("Open save folder", delegate { var v = Selected(); if (v != null) { Directory.CreateDirectory(v.Saves); Explore(v.Saves); } }));
        context.Items.Add(Item("Open game folder", delegate { var v = Selected(); if (v != null) Explore(v.Dir); }));
        context.Items.Add(Item("Mods...", delegate { var v = Selected(); if (v != null) Mods.Show(this, v); }));
        context.Items.Add(Item("Save backups...", delegate { var v = Selected(); if (v != null) Backups.Show(this, v, Reload); }));
        context.Items.Add(new ToolStripSeparator());
        context.Items.Add(Item("Import saves from suyu", delegate { ImportSuyu(); }));
        context.Items.Add(Item("Import saves from a folder...", delegate { ImportFolder(); }));
        context.Items.Add(Item("Copy saves from another version...", delegate { CopyFromVersion(); }));
        foreach (ToolStripItem i in context.Items) { i.ForeColor = Theme.Text; i.BackColor = Theme.Panel; }
        list.ContextMenuStrip = context;
        return list;
    }

    Control BuildBottom()
    {
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Back, Padding = new Padding(18, 12, 18, 12) };
        status.Dock = DockStyle.Fill;
        status.ForeColor = Theme.Muted;
        status.TextAlign = ContentAlignment.MiddleLeft;
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
                                          WrapContents = false, BackColor = Theme.Back };
        var label = new Label { Text = "Input", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, 10, 8, 0) };
        mode.DropDownStyle = ComboBoxStyle.DropDownList;
        mode.FlatStyle = FlatStyle.Flat;
        mode.BackColor = Theme.Panel;
        mode.ForeColor = Theme.Text;
        mode.Width = 320;
        mode.Margin = new Padding(0, 6, 14, 0);
        mode.Items.AddRange(ModeLabels);
        mode.DrawMode = DrawMode.OwnerDrawFixed;
        mode.ItemHeight = 22;
        mode.DrawItem += (s, e) => {
            if (e.Index < 0) return;
            bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(hot ? Theme.Select : Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, ModeLabels[e.Index], Font, Rectangle.Inflate(e.Bounds, -4, 0), Theme.Text,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        mode.SelectionChangeCommitted += delegate { SetMode(mode.SelectedIndex); };
        play.Text = "Play";
        play.Size = new Size(120, 38);
        play.FlatStyle = FlatStyle.Flat;
        play.FlatAppearance.BorderSize = 0;
        play.BackColor = Theme.Gold;
        play.ForeColor = Color.FromArgb(0x3A, 0x26, 0x05);
        play.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        play.Click += delegate { Play(); };
        var gear = new Button { Text = "Settings", Size = new Size(100, 38), FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel,
                                ForeColor = Theme.Text, Margin = new Padding(0, 0, 10, 0) };
        gear.FlatAppearance.BorderColor = Theme.Line;
        gear.Click += delegate { ShowSettings(); };
        right.Controls.AddRange(new Control[] { label, mode, gear, play });
        bottom.Controls.Add(status);
        bottom.Controls.Add(right);
        return bottom;
    }

    void Reload()
    {
        string keep = Selected() != null ? Selected().Name : settings.Get("last_version", "");
        versions = new List<GameVersion>();
        string dir = Path.Combine(root, "versions");
        if (Directory.Exists(dir))
            foreach (var d in Directory.GetDirectories(dir).OrderBy(d => d))
                if (File.Exists(Path.Combine(d, "SkyboundTOTK.exe")))
                    versions.Add(new GameVersion { Name = Path.GetFileName(d), Dir = d });
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var v in versions)
        {
            var item = new ListViewItem("The Legend of Zelda: Tears of the Kingdom") { ImageIndex = 0, Tag = v };
            item.SubItems.Add(v.Name);
            item.SubItems.Add(Played(v));
            item.SubItems.Add(LastPlayed(v));
            item.SubItems.Add(SaveSummary(v));
            list.Items.Add(item);
            if (v.Name == keep) item.Selected = true;
        }
        if (list.SelectedItems.Count == 0 && list.Items.Count > 0) list.Items[list.Items.Count - 1].Selected = true;
        list.EndUpdate();
        // (rows tall enough for the 48 px icon: the image list sets the height)
        UpdateButtons();
    }

    string Played(GameVersion v)
    {
        double minutes = double.Parse(settings.Get("played." + v.Name, "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (minutes < 1) return "never";
        return minutes < 60 ? ((int)minutes) + " min" : (minutes / 60).ToString("0.0") + " h";
    }

    string LastPlayed(GameVersion v)
    {
        long ticks = long.Parse(settings.Get("last." + v.Name, "0"));
        return ticks == 0 ? "never" : new DateTime(ticks).ToString("g");
    }

    static string SaveSummary(GameVersion v)
    {
        if (!Directory.Exists(v.Saves)) return "none yet";
        var slots = Directory.GetDirectories(v.Saves, "slot_*");
        if (slots.Length == 0) return "none yet";
        var newest = slots.Max(s => Directory.GetLastWriteTime(s));
        return slots.Length + " slot" + (slots.Length == 1 ? "" : "s") + ", newest " + newest.ToString("d");
    }

    GameVersion Selected()
    {
        return list.SelectedItems.Count > 0 ? (GameVersion)list.SelectedItems[0].Tag : null;
    }

    void UpdateButtons()
    {
        bool busy = running != null && !running.HasExited;
        play.Enabled = Selected() != null && !busy;
        play.BackColor = play.Enabled ? Theme.Gold : Theme.Line;
        if (versions.Count == 0)
            status.Text = "No game versions found in " + Path.Combine(root, "versions") + ". Build one with build_game.py.";
        else if (busy)
            status.Text = "Playing. F8 in the game switches the input mode.";
        else
            status.Text = versions.Count + " version" + (versions.Count == 1 ? "" : "s") + " installed. Double-click to play.";
    }

    void SetMode(int m)
    {
        if (m < 0) m = 0;
        settings.Set("input_mode", Modes[m]);
        mode.SelectedIndex = m;
        for (int i = 0; i < 3; i++) if (modeItems[i] != null) modeItems[i].Checked = i == m;
    }

    bool GameRunning() { return running != null && !running.HasExited; }

    void ShowSettings()
    {
        GameSettings.Show(this, settings, versions, ModeLabels, Math.Max(0, Array.IndexOf(Modes, settings.Get("input_mode", "both"))),
                          SetMode);
    }

    void Play()
    {
        var v = Selected();
        if (v == null || GameRunning()) return;
        Directory.CreateDirectory(v.User);
        if (settings.Get("backup_on_start", "1") == "1")
        {
            try { Backups.Make(v, "auto"); }
            catch (Exception e)
            {
                if (MessageBox.Show(this, "The save backup failed (" + e.Message + "). Play anyway?", "Skybound",
                                    MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            }
        }
        // (the game reads its mode from here at start; F8 in the game changes it and writes it back)
        File.WriteAllText(Path.Combine(v.User, "input_mode.txt"), settings.Get("input_mode", "both") + "\n");
        settings.Set("last_version", v.Name);
        settings.Set("last." + v.Name, DateTime.Now.Ticks.ToString());
        var start = DateTime.Now;
        try
        {
            var info = new ProcessStartInfo(v.Exe) { WorkingDirectory = v.Dir, UseShellExecute = false };
            GameSettings.Apply(settings, info);
            running = Process.Start(info);
            if (settings.Get("hide_while_playing", "1") == "1") WindowState = FormWindowState.Minimized;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Could not start " + v.Exe + ":\n" + e.Message, "Skybound", MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
            return;
        }
        running.EnableRaisingEvents = true;
        running.Exited += delegate {
            BeginInvoke((Action)delegate {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                double before = double.Parse(settings.Get("played." + v.Name, "0"), inv);
                settings.Set("played." + v.Name, (before + (DateTime.Now - start).TotalMinutes).ToString("0.0", inv));
                string m = File.Exists(Path.Combine(v.User, "input_mode.txt"))
                    ? File.ReadAllText(Path.Combine(v.User, "input_mode.txt")).Trim() : "";
                if (Array.IndexOf(Modes, m) >= 0) SetMode(Array.IndexOf(Modes, m));
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                // (a crash: the game's log is the place to look)
                if (running.ExitCode != 0 && (DateTime.Now - start).TotalSeconds > 2 &&
                    MessageBox.Show(this, "The game stopped unexpectedly (exit code " + running.ExitCode + "). Open its " +
                                    "folder to find the log?", "Skybound", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Explore(v.Dir);
                Reload();
            });
        };
        Reload();
    }

    // ---- saves: every import first copies the version's current saves to user\save-backups\<time> ----
    void ImportSuyu()
    {
        string root2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    @"suyu\nand\user\save\0000000000000000");
        string found = null;
        if (Directory.Exists(root2))
            found = Directory.GetDirectories(root2).Select(u => Path.Combine(u, "0100F2C0115B6000"))
                             .Where(Directory.Exists).OrderByDescending(Directory.GetLastWriteTime).FirstOrDefault();
        if (found == null)
        {
            MessageBox.Show(this, "No Tears of the Kingdom saves found in suyu (" + root2 + ").", "Skybound");
            return;
        }
        Import(found, "suyu");
    }

    void ImportFolder()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Pick the folder that holds slot_00, slot_01, ... (in Ryujinx: right-click the game, " +
                                 "Open User Save Directory, then the folder inside it)";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string dir = dialog.SelectedPath;
            if (Directory.GetDirectories(dir, "slot_*").Length == 0)
            {
                var inner = Directory.GetDirectories(dir).FirstOrDefault(d => Directory.GetDirectories(d, "slot_*").Length > 0);
                if (inner == null)
                {
                    MessageBox.Show(this, "That folder has no slot_ folders.", "Skybound");
                    return;
                }
                dir = inner;
            }
            Import(dir, "that folder");
        }
    }

    void CopyFromVersion()
    {
        var target = Selected();
        if (target == null) return;
        var others = versions.Where(v => v != target && Directory.Exists(v.Saves)).ToList();
        if (others.Count == 0)
        {
            MessageBox.Show(this, "No other version has saves yet.", "Skybound");
            return;
        }
        var source = others.Count == 1 ? others[0] : Pick(others);
        if (source == null) return;
        if (string.CompareOrdinal(source.Name, target.Name) > 0 &&
            MessageBox.Show(this, "Saves from " + source.Name + " may not load in the older " + target.Name +
                            ". Copy them anyway?", "Skybound", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Import(source.Saves, "version " + source.Name);
    }

    GameVersion Pick(List<GameVersion> options)
    {
        using (var f = new Form { Text = "Copy saves from", ClientSize = new Size(300, 120), BackColor = Theme.Back,
                                  ForeColor = Theme.Text, FormBorderStyle = FormBorderStyle.FixedDialog,
                                  StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(16, 16), Width = 268 };
            box.Items.AddRange(options.Select(o => (object)o.Name).ToArray());
            box.SelectedIndex = 0;
            var ok = new Button { Text = "Copy", DialogResult = DialogResult.OK, Location = new Point(184, 70), Width = 100,
                                  BackColor = Theme.Gold, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
            f.Controls.AddRange(new Control[] { box, ok });
            f.AcceptButton = ok;
            return f.ShowDialog(this) == DialogResult.OK ? options[box.SelectedIndex] : null;
        }
    }

    void Import(string source, string from)
    {
        var v = Selected();
        if (v == null) return;
        if (running != null && !running.HasExited)
        {
            MessageBox.Show(this, "Close the game first.", "Skybound");
            return;
        }
        if (MessageBox.Show(this, "Copy the saves from " + from + " into version " + v.Name + "?\n\n" + source +
                            "\n\nThe current saves of " + v.Name + " are backed up first.", "Import saves",
                            MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        Backups.Make(v, "import");
        Ui.CopyDir(source, v.Saves);
        Reload();
        MessageBox.Show(this, "Saves imported. Use Load on the title screen to pick a slot.", "Skybound");
    }

    void ShowControls()
    {
        MessageBox.Show(this,
            "Keyboard: WASD move, arrows or IJKL camera, Space jump, E action, Shift run, Q abilities, R, Z shield, " +
            "X or F bow, Ctrl crouch, C first person, Tab or Enter menu, M map, 1-4 d-pad.\n" +
            "Mouse: click the window for mouse camera (Esc releases), left attack, right shield. - and = sensitivity.\n" +
            "F8: switch input mode (controller and keyboard, controller only, keyboard only).",
            "Controls");
    }

    static void Explore(string dir)
    {
        Process.Start("explorer.exe", "\"" + dir + "\"");
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new LauncherForm());
    }
}
