using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms.Automation;
using Markdig;

namespace Daftari;

public class MainForm : Form
{
    readonly Settings settings = Settings.Load();
    Vault vault = null!;

    readonly SplitContainer split = new();
    readonly TreeView tree = new();
    readonly TextBox editor = new();
    readonly MenuStrip menu = new();
    readonly StatusStrip statusStrip = new();
    readonly ToolStripStatusLabel statusLabel = new();
    readonly ToolStripStatusLabel countLabel = new("");
    readonly System.Windows.Forms.Timer autosaveTimer = new() { Interval = 30_000 };
    readonly System.Windows.Forms.Timer countTimer = new() { Interval = 400 };

    string? currentNote;
    bool dirty;
    bool loading;
    string lastFind = "";

    // سجل التراجع والإعادة: حقل النص القياسي يدعم خطوة واحدة فقط، فنحتفظ بسجل كامل بأنفسنا.
    // التعديلات المتتابعة خلال فترة قصيرة تُجمع في خطوة واحدة كي لا يتراجع المستخدم حرفاً حرفاً.
    readonly List<(string Text, int Caret)> undoStack = new();
    readonly List<(string Text, int Caret)> redoStack = new();
    readonly System.Windows.Forms.Timer burstTimer = new() { Interval = 700 };
    string lastText = "";
    bool applyingHistory;
    bool editBurst;

    public MainForm()
    {
        L.En = settings.Language == "en";

        Text = L.T("دفتري", "Daftari");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(640, 440);
        Font = new Font("Segoe UI", 11f);
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        statusLabel.Text = L.T("جاهز", "Ready");

        BuildMenu();
        BuildLayout();
        OpenVault(ResolveVaultPath());

        autosaveTimer.Tick += (_, _) => SaveCurrent();
        autosaveTimer.Start();
        countTimer.Tick += (_, _) => { countTimer.Stop(); UpdateCount(); };
        burstTimer.Tick += (_, _) => { burstTimer.Stop(); editBurst = false; };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F6)
            {
                // من الشجرة إلى المحرر، ومن أي مكان آخر إلى الشجرة —
                // حتى لو ضاع التركيز في عنصر غير متوقع يبقى F6 مخرجاً مضموناً
                if (tree.Focused) editor.Focus(); else tree.Focus();
                e.SuppressKeyPress = true;
            }
            // Ctrl+Tab لا يصلح اختصار قائمة في WinForms، فنلتقطه على مستوى النافذة
            else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.Tab)
            {
                ToggleLastNote();
                e.SuppressKeyPress = true;
            }
        };

        FormClosing += (_, _) => { SaveCurrent(); settings.Save(); };
    }

    // ---------- بناء الواجهة ----------

    void BuildLayout()
    {
        tree.Dock = DockStyle.Fill;
        tree.HideSelection = false;
        tree.RightToLeftLayout = L.RtlLayout;
        tree.AccessibleName = L.T("شجرة الملاحظات", "Notes tree");
        tree.BorderStyle = BorderStyle.None;
        tree.BackColor = Color.FromArgb(245, 246, 248);
        tree.ItemHeight = 30;
        tree.ShowLines = false;
        tree.FullRowSelect = true;
        tree.AfterSelect += Tree_AfterSelect;
        tree.KeyDown += Tree_KeyDown;

        editor.Multiline = true;
        editor.WordWrap = settings.WordWrap;
        editor.ScrollBars = settings.WordWrap ? ScrollBars.Vertical : ScrollBars.Both;
        editor.AcceptsReturn = true;
        editor.AcceptsTab = true;
        editor.MaxLength = int.MaxValue;
        editor.HideSelection = false;
        editor.Dock = DockStyle.Fill;
        editor.BorderStyle = BorderStyle.None;
        editor.Font = new Font("Segoe UI", settings.FontSize);
        editor.AccessibleName = L.T("محرر الملاحظة", "Note editor");
        editor.RightToLeft = settings.EditorRightToLeft ? RightToLeft.Yes : RightToLeft.No;
        editor.TextChanged += (_, _) =>
        {
            if (loading) return;
            dirty = true;
            countTimer.Stop();
            countTimer.Start();
            if (applyingHistory) return;
            if (!editBurst)
            {
                undoStack.Add((lastText, editor.SelectionStart));
                // كل لقطة نسخة كاملة من النص، فنحد العمق حمايةً للذاكرة مع الملاحظات الكبيرة
                if (undoStack.Count > 100) undoStack.RemoveAt(0);
                redoStack.Clear();
                editBurst = true;
            }
            burstTimer.Stop();
            burstTimer.Start();
            lastText = editor.Text;
        };
        editor.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                editor.SelectAll();
                e.SuppressKeyPress = true;
            }
            // حقل النص القياسي يمرّر العرض فقط عند Ctrl+سهم دون تحريك المؤشر،
            // فننفذ التنقل بين الفقرات بأنفسنا ليتبعه NVDA.
            else if (e.Control && !e.Alt && !e.Shift &&
                     (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                MoveByParagraph(down: e.KeyCode == Keys.Down);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.Shift && !e.Alt &&
                     (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                SelectByParagraph(down: e.KeyCode == Keys.Down);
                e.SuppressKeyPress = true;
            }
            // القفز بين العناوين: البديل المتاح لطي الأقسام داخل المحرر
            else if (e.Alt && !e.Control && !e.Shift &&
                     (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                JumpHeading(down: e.KeyCode == Keys.Down);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.Z)
            {
                Redo();
                e.SuppressKeyPress = true;
            }
        };

        var editorHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Color.White
        };
        editorHost.Controls.Add(editor);

        split.Dock = DockStyle.Fill;
        split.SplitterWidth = 6;
        split.BackColor = Color.FromArgb(226, 228, 232);
        split.Panel1.BackColor = tree.BackColor;
        split.Panel1.Padding = new Padding(6);
        split.SplitterDistance = 280;
        split.Panel1.Controls.Add(tree);
        split.Panel2.Controls.Add(editorHost);

        statusStrip.SizingGrip = false;
        statusStrip.Items.Add(statusLabel);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(countLabel);

        Controls.Add(split);
        Controls.Add(statusStrip);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    static ToolStripMenuItem MI(string text, Keys keys, EventHandler onClick, string? shortcutText = null)
    {
        var mi = new ToolStripMenuItem(text, null, onClick);
        if (keys != Keys.None) mi.ShortcutKeys = keys;
        if (shortcutText != null) mi.ShortcutKeyDisplayString = shortcutText;
        return mi;
    }

    void BuildMenu()
    {
        var file = new ToolStripMenuItem(L.T("&ملف", "&File"));
        file.DropDownItems.Add(MI(L.T("ملاحظة جديدة...", "New note..."), Keys.Control | Keys.N, (_, _) => NewNote()));
        file.DropDownItems.Add(MI(L.T("مجلد جديد...", "New folder..."), Keys.Control | Keys.Shift | Keys.N, (_, _) => NewFolder()));
        file.DropDownItems.Add(MI(L.T("إعادة تسمية...", "Rename..."), Keys.F2, (_, _) => RenameSelected()));
        file.DropDownItems.Add(MI(L.T("حذف (نقل إلى المحذوفات)", "Delete (move to trash)"), Keys.None, (_, _) => DeleteSelected(), "Delete"));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MI(L.T("حفظ", "Save"), Keys.Control | Keys.S, (_, _) => SaveCurrent(announce: true)));
        file.DropDownItems.Add(MI(L.T("تحديث الشجرة", "Refresh tree"), Keys.F5, (_, _) => { LoadTree(); Announce(L.T("تم تحديث شجرة الملاحظات", "Notes tree refreshed")); }));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MI(L.T("نسخة احتياطية الآن", "Back up now"), Keys.Control | Keys.Shift | Keys.B, (_, _) => BackupNow()));
        file.DropDownItems.Add(MI(L.T("الإعدادات...", "Settings..."), Keys.Control | Keys.Oemcomma, (_, _) => OpenSettings(), "Ctrl+,"));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MI(L.T("فتح قبو آخر...", "Open another vault..."), Keys.Control | Keys.O, (_, _) => ChooseVault()));
        file.DropDownItems.Add(MI(L.T("فتح مجلد القبو في مستكشف الملفات", "Open vault folder in File Explorer"), Keys.None, (_, _) => Process.Start("explorer.exe", vault.Root)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MI(L.T("خروج", "Exit"), Keys.None, (_, _) => Close(), "Alt+F4"));

        var edit = new ToolStripMenuItem(L.T("&تحرير", "&Edit"));
        edit.DropDownItems.Add(MI(L.T("تراجع", "Undo"), Keys.Control | Keys.Z, (_, _) => Undo()));
        edit.DropDownItems.Add(MI(L.T("إعادة", "Redo"), Keys.Control | Keys.Y, (_, _) => Redo()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(MI(L.T("بحث في الملاحظة...", "Find in note..."), Keys.Control | Keys.F, (_, _) => FindInNote()));
        edit.DropDownItems.Add(MI(L.T("العثور على التالي", "Find next"), Keys.F3, (_, _) => FindNext()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(MI(L.T("إدراج رابط لملاحظة...", "Insert link to a note..."), Keys.Control | Keys.K, (_, _) => InsertLink()));
        edit.DropDownItems.Add(MI(L.T("إدراج كتلة كود...", "Insert code block..."), Keys.Control | Keys.Shift | Keys.K, (_, _) => InsertCodeBlock()));
        edit.DropDownItems.Add(MI(L.T("جدول: إنشاء أو تحرير...", "Table: create or edit..."), Keys.Control | Keys.Shift | Keys.G, (_, _) => EditTable()));
        edit.DropDownItems.Add(MI(L.T("إدراج التاريخ والوقت", "Insert date and time"), Keys.Control | Keys.Shift | Keys.T, (_, _) => InsertTimestamp()));
        edit.DropDownItems.Add(MI(L.T("نسخ الملاحظة كاملة", "Copy entire note"), Keys.Control | Keys.Shift | Keys.C, (_, _) => CopyNote()));
        edit.DropDownItems.Add(MI(L.T("معاينة HTML في المتصفح", "HTML preview in browser"), Keys.Control | Keys.Shift | Keys.H, (_, _) => PreviewHtml()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(MI(L.T("تبديل اتجاه النص", "Toggle text direction"), Keys.Control | Keys.Shift | Keys.D, (_, _) => ToggleDirection()));
        edit.DropDownItems.Add(MI(L.T("تبديل التفاف الأسطر", "Toggle word wrap"), Keys.Control | Keys.Shift | Keys.W, (_, _) => ToggleWrap()));
        edit.DropDownItems.Add(MI(L.T("تكبير الخط", "Increase font size"), Keys.Control | Keys.Oemplus, (_, _) => ChangeFont(+1), "Ctrl+="));
        edit.DropDownItems.Add(MI(L.T("تصغير الخط", "Decrease font size"), Keys.Control | Keys.OemMinus, (_, _) => ChangeFont(-1), "Ctrl+-"));

        var nav = new ToolStripMenuItem(L.T("&انتقال", "&Navigate"));
        nav.DropDownItems.Add(MI(L.T("فتح ملاحظة بسرعة...", "Quick open note..."), Keys.Control | Keys.P, (_, _) => QuickOpen()));
        nav.DropDownItems.Add(MI(L.T("الملاحظة السابقة", "Previous note"), Keys.None, (_, _) => ToggleLastNote(), "Ctrl+Tab"));
        nav.DropDownItems.Add(MI(L.T("الملاحظات الأخيرة...", "Recent notes..."), Keys.Control | Keys.R, (_, _) => ShowRecent()));
        nav.DropDownItems.Add(MI(L.T("أين أنا؟", "Where am I?"), Keys.Control | Keys.I, (_, _) => AnnounceWhereAmI()));
        nav.DropDownItems.Add(MI(L.T("اتباع الرابط عند المؤشر", "Follow link at caret"), Keys.Control | Keys.Enter, (_, _) => FollowLink()));
        nav.DropDownItems.Add(MI(L.T("الروابط الواردة...", "Backlinks..."), Keys.Control | Keys.B, (_, _) => ShowBacklinks()));
        nav.DropDownItems.Add(MI(L.T("عناوين الملاحظة...", "Note headings..."), Keys.Control | Keys.J, (_, _) => ShowHeadings()));
        nav.DropDownItems.Add(MI(L.T("الوسوم...", "Tags..."), Keys.Control | Keys.T, (_, _) => ShowTags()));
        nav.DropDownItems.Add(MI(L.T("ملاحظة اليوم", "Today's note"), Keys.Control | Keys.D, (_, _) => OpenDailyNote()));
        nav.DropDownItems.Add(new ToolStripSeparator());
        nav.DropDownItems.Add(MI(L.T("البحث في كل الملاحظات...", "Search all notes..."), Keys.Control | Keys.Shift | Keys.F, (_, _) => SearchVault()));
        nav.DropDownItems.Add(new ToolStripSeparator());
        nav.DropDownItems.Add(MI(L.T("الانتقال إلى المحرر", "Go to editor"), Keys.Control | Keys.E, (_, _) => editor.Focus()));
        nav.DropDownItems.Add(MI(L.T("الانتقال إلى شجرة الملاحظات", "Go to notes tree"), Keys.Control | Keys.Shift | Keys.E, (_, _) => tree.Focus()));

        var help = new ToolStripMenuItem(L.T("&مساعدة", "&Help"));
        help.DropDownItems.Add(MI(L.T("الاختصارات", "Shortcuts"), Keys.F1, (_, _) => new HelpForm(HelpText).ShowDialog(this)));
        help.DropDownItems.Add(MI(L.T("حول دفتري", "About Daftari"), Keys.None, (_, _) =>
            Msg(L.T("دفتري — تطبيق ملاحظات عربي متوافق مع قارئ الشاشة NVDA.\nالملاحظات ملفات Markdown عادية داخل مجلد القبو، متوافقة مع Obsidian.",
                    "Daftari — an Arabic-first note-taking app built for the NVDA screen reader.\nNotes are plain Markdown files inside the vault folder, compatible with Obsidian."))));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, nav, help });
    }

    // ---------- القبو والشجرة ----------

    string ResolveVaultPath()
    {
        if (!string.IsNullOrWhiteSpace(settings.VaultPath) && Directory.Exists(settings.VaultPath))
            return settings.VaultPath;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "دفتري");
    }

    void OpenVault(string path)
    {
        vault = new Vault(path);
        settings.VaultPath = path;
        settings.Save();
        if (!vault.AllNotes().Any())
            vault.CreateNote(vault.Root, L.T("أهلاً بك", "Welcome"), WelcomeText);
        currentNote = null;
        loading = true;
        editor.Text = "";
        loading = false;
        dirty = false;
        ResetHistory();
        LoadTree();
        var first = vault.AllNotes().OrderBy(p => p).FirstOrDefault();
        if (first != null) OpenNote(first);
    }

    void ChooseVault()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = L.T("اختر مجلد القبو (سيُنشأ إن لم يكن موجوداً)", "Choose the vault folder (created if missing)"),
            UseDescriptionForTitle = true,
            SelectedPath = vault.Root
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SaveCurrent();
            OpenVault(dlg.SelectedPath);
            Announce(L.T($"تم فتح القبو {Path.GetFileName(vault.Root)}", $"Opened vault {Path.GetFileName(vault.Root)}"));
        }
    }

    void LoadTree()
    {
        var selected = currentNote;
        tree.BeginUpdate();
        tree.Nodes.Clear();
        var rootNode = new TreeNode(Path.GetFileName(vault.Root)) { Tag = vault.Root };
        AddChildren(rootNode, vault.Root);
        tree.Nodes.Add(rootNode);
        rootNode.Expand();
        tree.EndUpdate();
        if (selected != null) SelectNodeFor(selected);
    }

    void AddChildren(TreeNode parent, string dir)
    {
        IEnumerable<string> dirs, files;
        try
        {
            dirs = Directory.GetDirectories(dir).OrderBy(x => Path.GetFileName(x), StringComparer.CurrentCultureIgnoreCase);
            files = Directory.GetFiles(dir, "*.md").OrderBy(x => Path.GetFileName(x), StringComparer.CurrentCultureIgnoreCase);
        }
        catch { return; }

        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.') || string.Equals(name, Vault.TrashFolderName, StringComparison.OrdinalIgnoreCase))
                continue;
            var n = new TreeNode(name) { Tag = d };
            AddChildren(n, d);
            parent.Nodes.Add(n);
        }
        foreach (var f in files)
            parent.Nodes.Add(new TreeNode(Path.GetFileNameWithoutExtension(f)) { Tag = f });
    }

    TreeNode? FindNode(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode n in nodes)
        {
            if (string.Equals(n.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return n;
            var found = FindNode(n.Nodes, path);
            if (found != null) return found;
        }
        return null;
    }

    void SelectNodeFor(string path)
    {
        var node = FindNode(tree.Nodes, path);
        if (node != null && tree.SelectedNode != node)
            tree.SelectedNode = node;
    }

    void Tree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        // الفتح من الشجرة صامت: NVDA يعلن عقدة الشجرة بنفسه،
        // وإعلاننا "فُتحت الملاحظة" كان يقاطعه فتبدو الأسهم معطلة
        if (e.Node?.Tag is string path && File.Exists(path) &&
            !string.Equals(path, currentNote, StringComparison.OrdinalIgnoreCase))
            OpenNote(path, announceOpen: false);
    }

    void Tree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Enter)
        {
            if (tree.SelectedNode?.Tag is string p && File.Exists(p)) editor.Focus();
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// يجبر انتقال تركيز حقيقياً إلى المحرر بعد إغلاق الحوارات النمطية.
    /// استدعاء Focus مباشرة بعد إغلاق حوار قد لا يُنتج حدث تركيز جديداً،
    /// فيفقد NVDA التتبع: لا يردد الأحرف المكتوبة ولا يتابع حركة الأسهم.
    /// نؤجل بالطابور ثم نفلت التركيز ونمسكه من جديد ليصدر حدث لا يفوته NVDA.
    /// </summary>
    void FocusEditorFresh(string announcement = "")
    {
        BeginInvoke(() =>
        {
            ActiveControl = null;
            editor.Focus();
            if (announcement.Length > 0) Announce(announcement, interrupt: false);
        });
    }

    // ---------- فتح وحفظ ----------

    void OpenNote(string path, int line = -1, bool announceOpen = true)
    {
        SaveCurrent();
        if (!File.Exists(path)) { Announce(L.T("الملف غير موجود", "File not found")); return; }
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { Msg(L.T("تعذر فتح الملاحظة: ", "Could not open the note: ") + ex.Message); return; }

        loading = true;
        editor.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        loading = false;
        currentNote = path;
        dirty = false;
        ResetHistory();
        Text = $"{vault.DisplayName(path)} — {L.T("دفتري", "Daftari")}";

        editor.SelectionStart = line >= 0 ? LineStartIndex(editor.Text, line) : 0;
        editor.SelectionLength = 0;
        editor.ScrollToCaret();

        settings.RecentNotes.Remove(path);
        settings.RecentNotes.Insert(0, path);
        if (settings.RecentNotes.Count > 15)
            settings.RecentNotes.RemoveRange(15, settings.RecentNotes.Count - 15);

        SelectNodeFor(path);
        UpdateCount();
        if (announceOpen)
            Announce(L.T($"فُتحت الملاحظة {vault.DisplayName(path)}", $"Opened note {vault.DisplayName(path)}"));
    }

    void SaveCurrent(bool announce = false)
    {
        if (currentNote == null || !dirty)
        {
            if (announce) Announce(L.T("لا توجد تغييرات للحفظ", "No changes to save"));
            return;
        }
        try
        {
            File.WriteAllText(currentNote, editor.Text, new UTF8Encoding(false));
            dirty = false;
            if (announce) Announce(L.T("تم الحفظ", "Saved"));
        }
        catch (Exception ex) { Msg(L.T("تعذر الحفظ: ", "Could not save: ") + ex.Message); }
    }

    // ---------- إنشاء وإعادة تسمية وحذف ----------

    string TargetFolder()
    {
        if (tree.SelectedNode?.Tag is string p)
        {
            if (Directory.Exists(p)) return p;
            if (File.Exists(p)) return Path.GetDirectoryName(p)!;
        }
        return vault.Root;
    }

    void NewNote()
    {
        var name = InputBox.Show(this, L.T("ملاحظة جديدة", "New note"), L.T("اسم الملاحظة الجديدة:", "Name of the new note:"));
        if (name == null) return;
        var path = vault.CreateNote(TargetFolder(), name, $"# {name}\r\n\r\n");
        // الفتح وتحديث الشجرة أولاً، ثم انتقال تركيز حقيقي مؤجل إلى المحرر
        // يعقبه إعلان غير مقاطع كي يُسمعا معاً بالترتيب
        OpenNote(path, announceOpen: false);
        editor.SelectionStart = editor.TextLength;
        LoadTree();
        FocusEditorFresh(L.T($"أُنشئت الملاحظة {vault.DisplayName(path)}", $"Created note {vault.DisplayName(path)}"));
    }

    void NewFolder()
    {
        var name = InputBox.Show(this, L.T("مجلد جديد", "New folder"), L.T("اسم المجلد الجديد:", "Name of the new folder:"));
        if (name == null) return;
        var path = Path.Combine(TargetFolder(), Vault.Sanitize(name));
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) { Msg(L.T("تعذر إنشاء المجلد: ", "Could not create the folder: ") + ex.Message); return; }
        LoadTree();
        var node = FindNode(tree.Nodes, path);
        if (node != null) { tree.SelectedNode = node; tree.Focus(); }
        Announce(L.T($"أُنشئ المجلد {name}", $"Created folder {name}"));
    }

    void RenameSelected()
    {
        string? path = tree.SelectedNode?.Tag as string ?? currentNote;
        if (path == null || string.Equals(path, vault.Root, StringComparison.OrdinalIgnoreCase))
        {
            Announce(L.T("لا يوجد عنصر محدد لإعادة التسمية", "Nothing selected to rename"));
            return;
        }
        bool isFile = File.Exists(path);
        var oldName = isFile ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
        var newName = InputBox.Show(this, L.T("إعادة تسمية", "Rename"), L.T("الاسم الجديد:", "New name:"), oldName);
        if (newName == null || newName == oldName) return;
        newName = Vault.Sanitize(newName);
        var dest = Path.Combine(Path.GetDirectoryName(path)!, isFile ? newName + ".md" : newName);
        if (File.Exists(dest) || Directory.Exists(dest))
        {
            Msg(L.T("يوجد عنصر بهذا الاسم بالفعل.", "An item with this name already exists."));
            return;
        }
        try
        {
            SaveCurrent();
            if (isFile) File.Move(path, dest);
            else Directory.Move(path, dest);
        }
        catch (Exception ex) { Msg(L.T("تعذرت إعادة التسمية: ", "Could not rename: ") + ex.Message); return; }

        if (currentNote != null)
        {
            if (isFile && string.Equals(currentNote, path, StringComparison.OrdinalIgnoreCase))
                currentNote = dest;
            else if (!isFile && currentNote.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                currentNote = dest + currentNote[path.Length..];
        }

        // تحديث كل روابط [[الاسم القديم]] في القبو كي لا تنكسر بصمت
        int updatedLinks = isFile ? vault.UpdateLinks(oldName, newName) : 0;
        if (updatedLinks > 0 && currentNote != null && File.Exists(currentNote))
        {
            // الملاحظة المفتوحة قد تكون من الملفات المحدّثة على القرص — نعيد تحميلها بموضع المؤشر
            int caret = editor.SelectionStart;
            loading = true;
            editor.Text = File.ReadAllText(currentNote).Replace("\r\n", "\n").Replace("\n", "\r\n");
            loading = false;
            dirty = false;
            ResetHistory();
            editor.Select(Math.Min(caret, editor.TextLength), 0);
        }

        LoadTree();
        if (currentNote != null) Text = $"{vault.DisplayName(currentNote)} — {L.T("دفتري", "Daftari")}";
        Announce(updatedLinks > 0
            ? L.T($"تمت إعادة التسمية إلى {newName} وتحديث {updatedLinks} رابط يشير إليها",
                  $"Renamed to {newName} and updated {updatedLinks} links pointing to it")
            : L.T($"تمت إعادة التسمية إلى {newName}", $"Renamed to {newName}"));
    }

    void DeleteSelected()
    {
        if (tree.SelectedNode?.Tag is not string path ||
            string.Equals(path, vault.Root, StringComparison.OrdinalIgnoreCase))
        {
            Announce(L.T("لا يوجد عنصر محدد للحذف", "Nothing selected to delete"));
            return;
        }
        var name = tree.SelectedNode.Text;
        var kind = Directory.Exists(path) ? L.T("المجلد", "folder") : L.T("الملاحظة", "note");
        var answer = MessageBox.Show(this,
            L.T($"هل تريد نقل {kind} \"{name}\" إلى مجلد المحذوفات داخل القبو؟",
                $"Move the {kind} \"{name}\" to the trash folder inside the vault?"),
            L.T("تأكيد الحذف", "Confirm delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2, L.MsgOptions);
        if (answer != DialogResult.Yes) return;

        bool wasCurrent = currentNote != null &&
            (string.Equals(currentNote, path, StringComparison.OrdinalIgnoreCase) ||
             currentNote.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (wasCurrent) { dirty = false; currentNote = null; }

        try { vault.MoveToTrash(path); }
        catch (Exception ex) { Msg(L.T("تعذر الحذف: ", "Could not delete: ") + ex.Message); return; }

        if (wasCurrent)
        {
            loading = true;
            editor.Text = "";
            loading = false;
            ResetHistory();
            Text = L.T("دفتري", "Daftari");
        }
        LoadTree();
        tree.Focus();
        Announce(L.T($"نُقل {kind} {name} إلى المحذوفات", $"Moved {kind} {name} to trash"));
    }

    // ---------- الروابط ----------

    /// <summary>يستخرج هدف رابط الويكي المحيط بمؤشر الكتابة إن وجد.</summary>
    string? LinkAtCaret()
    {
        var text = editor.Text;
        if (text.Length == 0) return null;
        int pos = Math.Min(editor.SelectionStart, text.Length - 1);
        int open = text.LastIndexOf("[[", pos, StringComparison.Ordinal);
        if (open < 0) return null;
        int close = text.IndexOf("]]", open + 2, StringComparison.Ordinal);
        if (close < 0 || editor.SelectionStart > close + 2) return null;
        var inner = text[(open + 2)..close];
        int cut = inner.IndexOfAny(new[] { '|', '#' });
        if (cut >= 0) inner = inner[..cut];
        inner = inner.Trim();
        return inner.Length > 0 ? inner : null;
    }

    void FollowLink()
    {
        var target = LinkAtCaret();
        if (target == null)
        {
            Announce(L.T("لا يوجد رابط عند مؤشر الكتابة. ضع المؤشر داخل رابط بين قوسين مزدوجين ثم أعد المحاولة.",
                         "No link at the caret. Place the caret inside a double-bracket link and try again."));
            return;
        }
        var path = vault.ResolveLink(target);
        bool created = false;
        if (path == null)
        {
            var answer = MessageBox.Show(this,
                L.T($"الملاحظة \"{target}\" غير موجودة. هل تريد إنشاءها؟", $"The note \"{target}\" does not exist. Create it?"),
                L.T("إنشاء ملاحظة", "Create note"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1, L.MsgOptions);
            if (answer != DialogResult.Yes) return;
            path = vault.CreateNote(vault.Root, target, $"# {target}\r\n\r\n");
            created = true;
        }
        OpenNote(path);
        if (created) LoadTree();
        FocusEditorFresh();
    }

    void InsertLink()
    {
        var items = vault.AllNotes().Select(p => (vault.DisplayName(p), p));
        using var picker = new NotePickerForm(L.T("إدراج رابط", "Insert link"),
            L.T("ابحث عن ملاحظة لإدراج رابط لها:", "Search for a note to link to:"), items, allowCreate: false);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedPath == null) return;
        var name = vault.DisplayName(picker.SelectedPath);
        editor.SelectedText = $"[[{name}]]";
        FocusEditorFresh(L.T($"أُدرج رابط إلى {name}", $"Inserted a link to {name}"));
    }

    void QuickOpen()
    {
        var items = vault.AllNotes().Select(p => (vault.RelativeName(p), p));
        using var picker = new NotePickerForm(L.T("فتح ملاحظة", "Open note"),
            L.T("اكتب جزءاً من اسم الملاحظة:", "Type part of the note name:"), items, allowCreate: true);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.SelectedPath != null)
        {
            OpenNote(picker.SelectedPath);
            FocusEditorFresh();
        }
        else if (picker.CreateName != null)
        {
            var path = vault.CreateNote(vault.Root, picker.CreateName, $"# {picker.CreateName}\r\n\r\n");
            OpenNote(path, announceOpen: false);
            LoadTree();
            FocusEditorFresh(L.T($"أُنشئت الملاحظة {vault.DisplayName(path)}", $"Created note {vault.DisplayName(path)}"));
        }
    }

    void ShowBacklinks()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        SaveCurrent();
        var hits = vault.Backlinks(currentNote).ToList();
        if (hits.Count == 0) { Announce(L.T("لا توجد روابط واردة لهذه الملاحظة", "No backlinks to this note")); return; }
        using var dlg = new ListPickForm(
            L.T($"الروابط الواردة إلى {vault.DisplayName(currentNote)} ({hits.Count})",
                $"Backlinks to {vault.DisplayName(currentNote)} ({hits.Count})"),
            L.T("قائمة الروابط الواردة", "Backlink list"));
        foreach (var h in hits)
        {
            var snippet = h.LineText.Length > 80 ? h.LineText[..80] + "…" : h.LineText;
            dlg.AddItem($"{vault.RelativeName(h.FilePath)} — {snippet}", h);
        }
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is SearchHit hit)
        {
            OpenNote(hit.FilePath, hit.LineNumber);
            FocusEditorFresh();
        }
    }

    // ---------- التنقل داخل الملاحظة ----------

    /// <summary>فهرس أول حرف في السطر المنطقي المعطى، بمعزل عن الالتفاف البصري للأسطر الطويلة.</summary>
    static int LineStartIndex(string text, int line)
    {
        int idx = 0;
        while (line-- > 0)
        {
            int nl = text.IndexOf('\n', idx);
            if (nl < 0) return text.Length;
            idx = nl + 1;
        }
        return idx;
    }

    /// <summary>رقم السطر المنطقي الذي يقع فيه الفهرس المعطى.</summary>
    static int LineFromIndex(string text, int index)
    {
        int count = 0;
        int limit = Math.Min(index, text.Length);
        for (int i = 0; i < limit; i++)
            if (text[i] == '\n') count++;
        return count;
    }

    static bool IsBlankLine(string s) => s.Trim().Length == 0;

    /// <summary>
    /// يحسب فهرس بداية الفقرة التالية أو السابقة انطلاقاً من فهرس معيّن، دون تحريك المؤشر.
    /// كل صف جدول يُعامل فقرةً مستقلة كي يتنقل المستخدم بين الصفوف، وصفوف الفواصل (---) تُتخطى.
    /// </summary>
    (int Index, bool AtBoundary) ParagraphTarget(string text, string[] lines, int fromIndex, bool down)
    {
        int line = Math.Min(LineFromIndex(text, fromIndex), lines.Length - 1);
        static bool Skip(string s) => IsBlankLine(s) || IsSeparatorRow(s);

        if (down)
        {
            int i = line;
            if (IsTableLine(lines[i])) i++;                          // صف الجدول وحدة واحدة
            else while (i < lines.Length && !Skip(lines[i]) && !IsTableLine(lines[i])) i++;
            while (i < lines.Length && Skip(lines[i])) i++;          // تخطي الفراغات والفواصل
            if (i >= lines.Length) return (text.Length, true);
            return (LineStartIndex(text, i), false);
        }
        else
        {
            int i = line;
            while (i > 0 && Skip(lines[i])) i--;                     // الخروج من الفراغات
            if (!IsTableLine(lines[i]))
                while (i > 0 && !Skip(lines[i - 1]) && !IsTableLine(lines[i - 1])) i--; // بداية الفقرة الحالية
            int start = LineStartIndex(text, i);
            if (start >= fromIndex)
            {
                // المؤشر في بداية الوحدة أصلاً، فالهدف هو الوحدة السابقة
                int j = i - 1;
                while (j >= 0 && Skip(lines[j])) j--;
                if (j < 0) return (0, true);
                if (!IsTableLine(lines[j]))
                    while (j > 0 && !Skip(lines[j - 1]) && !IsTableLine(lines[j - 1])) j--;
                return (LineStartIndex(text, j), false);
            }
            return (start, false);
        }
    }

    /// <summary>يجهّز نص سطر للنطق: صفوف الجداول تُقرأ كخلايا مفصولة بفواصل بدل أعواد |.</summary>
    static string LineForSpeech(string line)
    {
        if (!IsTableLine(line)) return line;
        var cells = SplitTableRow(line).Where(c => c.Length > 0).ToArray();
        return cells.Length > 0
            ? string.Join(L.T("، ", ", "), cells)
            : L.T("صف فارغ", "Empty row");
    }

    /// <summary>ينقل مؤشر الكتابة إلى بداية الفقرة التالية أو السابقة (الفقرات تفصلها أسطر فارغة).</summary>
    void MoveByParagraph(bool down)
    {
        var lines = editor.Lines;
        if (lines.Length == 0) return;
        var text = editor.Text;
        var (target, boundary) = ParagraphTarget(text, lines, editor.SelectionStart, down);
        editor.Select(target, 0);
        editor.ScrollToCaret();
        if (boundary)
        {
            Announce(down
                ? L.T("نهاية الملاحظة", "End of note")
                : IsBlankLine(lines[0])
                    ? L.T("بداية الملاحظة", "Start of note")
                    : L.T($"بداية الملاحظة. {LineForSpeech(lines[0])}", $"Start of note. {LineForSpeech(lines[0])}"));
            return;
        }
        // نعلن الفقرة الهدف بأنفسنا في كل انتقال: قراءة NVDA التلقائية بعد التحريك
        // البرمجي للمؤشر غير موثوقة (كانت تصمت عند السطر الأول والفقرة الأخيرة)
        int targetLine = Math.Min(LineFromIndex(text, target), lines.Length - 1);
        Announce(LineForSpeech(lines[targetLine]));
    }

    int selectionAnchor = -1;

    /// <summary>يوسّع التحديد أو يقلّصه حتى حدود الفقرة التالية أو السابقة (Ctrl+Shift+سهم).</summary>
    void SelectByParagraph(bool down)
    {
        var lines = editor.Lines;
        if (lines.Length == 0) return;
        var text = editor.Text;
        int selStart = editor.SelectionStart, selLen = editor.SelectionLength;

        // نقطة الارتكاز: المؤشر إن لم يكن هناك تحديد، وإلا الطرف الثابت من التحديد الحالي
        int anchor = selLen == 0 ? selStart
            : (selectionAnchor == selStart || selectionAnchor == selStart + selLen) ? selectionAnchor
            : selStart;
        int activeEnd = anchor == selStart && selLen > 0 ? selStart + selLen : selStart;

        var (target, _) = ParagraphTarget(text, lines, activeEnd, down);
        selectionAnchor = anchor;
        editor.Select(Math.Min(anchor, target), Math.Abs(target - anchor));
        editor.ScrollToCaret();
    }

    /// <summary>يقفز إلى العنوان التالي أو السابق في المحرر ويعلنه — بديل طي الأقسام داخل المحرر.</summary>
    void JumpHeading(bool down)
    {
        var lines = editor.Lines;
        if (lines.Length == 0) return;
        var text = editor.Text;
        int cur = Math.Min(LineFromIndex(text, editor.SelectionStart), lines.Length - 1);

        if (down)
        {
            for (int i = cur + 1; i < lines.Length; i++)
                if (lines[i].TrimStart().StartsWith('#')) { GoTo(i); return; }
            Announce(L.T("لا توجد عناوين تالية", "No next heading"));
        }
        else
        {
            for (int i = cur - 1; i >= 0; i--)
                if (lines[i].TrimStart().StartsWith('#')) { GoTo(i); return; }
            Announce(L.T("لا توجد عناوين سابقة", "No previous heading"));
        }

        void GoTo(int line)
        {
            editor.Select(LineStartIndex(text, line), 0);
            editor.ScrollToCaret();
            Announce(lines[line].TrimStart('#', ' ', '\t'));
        }
    }

    void ShowHeadings()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        var lines = editor.Lines;
        using var dlg = new ListPickForm(L.T("عناوين الملاحظة", "Note headings"), L.T("قائمة العناوين", "Heading list"));
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith('#'))
            {
                int level = t.TakeWhile(c => c == '#').Count();
                var title = t.TrimStart('#').Trim();
                if (title.Length > 0)
                    dlg.AddItem(L.T($"{title} (مستوى {level}، السطر {i + 1})", $"{title} (level {level}, line {i + 1})"), i);
            }
        }
        if (dlg.Count == 0) { Announce(L.T("لا توجد عناوين في هذه الملاحظة", "No headings in this note")); return; }
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is int line)
        {
            editor.Select(LineStartIndex(editor.Text, line), 0);
            editor.ScrollToCaret();
            FocusEditorFresh();
        }
    }

    void ShowTags()
    {
        SaveCurrent();
        var tags = vault.AllTags();
        if (tags.Count == 0)
        {
            Announce(L.T("لا توجد وسوم في القبو. اكتب #وسم داخل أي ملاحظة.", "No tags in the vault. Write #tag inside any note."));
            return;
        }
        using var dlg = new TagsForm(vault, tags);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedNote != null)
        {
            OpenNote(dlg.SelectedNote);
            FocusEditorFresh();
        }
    }

    void OpenDailyNote()
    {
        var folder = Path.Combine(vault.Root, "اليوميات");
        Directory.CreateDirectory(folder);
        var name = DateTime.Now.ToString("yyyy-MM-dd");
        var path = Path.Combine(folder, name + ".md");
        bool created = !File.Exists(path);
        if (created)
            File.WriteAllText(path, $"# {name}\r\n\r\n", new UTF8Encoding(false));
        OpenNote(path);
        editor.SelectionStart = editor.TextLength;
        editor.Focus();
        if (created) LoadTree();
    }

    void SearchVault()
    {
        SaveCurrent();
        using var dlg = new VaultSearchForm(vault);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected is SearchHit hit)
        {
            OpenNote(hit.FilePath, hit.LineNumber);
            FocusEditorFresh();
        }
    }

    void FindInNote()
    {
        var q = InputBox.Show(this, L.T("بحث في الملاحظة", "Find in note"), L.T("البحث عن:", "Find:"), lastFind);
        if (q == null) return;
        lastFind = q;
        FindNext();
        FocusEditorFresh(); // بعد إغلاق حوار البحث قد يفقد NVDA تتبع المحرر
    }

    void FindNext()
    {
        if (lastFind.Length == 0) { FindInNote(); return; }
        var text = editor.Text;
        int start = editor.SelectionStart + editor.SelectionLength;
        int idx = text.IndexOf(lastFind, Math.Min(start, text.Length), StringComparison.CurrentCultureIgnoreCase);
        if (idx < 0) idx = text.IndexOf(lastFind, 0, StringComparison.CurrentCultureIgnoreCase);
        if (idx < 0)
        {
            Announce(L.T($"لم يتم العثور على \"{lastFind}\"", $"\"{lastFind}\" not found"));
            return;
        }
        editor.SelectionStart = idx;
        editor.SelectionLength = lastFind.Length;
        editor.ScrollToCaret();
        editor.Focus();
        Announce(L.T($"عُثر عليه في السطر {LineFromIndex(text, idx) + 1}", $"Found at line {LineFromIndex(text, idx) + 1}"));
    }

    // ---------- التراجع والإعادة ----------

    void ResetHistory()
    {
        undoStack.Clear();
        redoStack.Clear();
        lastText = editor.Text;
        editBurst = false;
        burstTimer.Stop();
    }

    void Undo()
    {
        if (undoStack.Count == 0) { Announce(L.T("لا شيء للتراجع عنه", "Nothing to undo")); return; }
        var (text, caret) = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        redoStack.Add((editor.Text, editor.SelectionStart));
        ApplyHistory(text, caret, L.T("تراجع", "Undo"));
    }

    void Redo()
    {
        if (redoStack.Count == 0) { Announce(L.T("لا شيء للإعادة", "Nothing to redo")); return; }
        var (text, caret) = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        undoStack.Add((editor.Text, editor.SelectionStart));
        ApplyHistory(text, caret, L.T("إعادة", "Redo"));
    }

    void ApplyHistory(string text, int caret, string action)
    {
        applyingHistory = true;
        editor.Text = text;
        applyingHistory = false;
        lastText = text;
        editBurst = false;
        burstTimer.Stop();
        int pos = Math.Min(caret, text.Length);
        editor.Select(pos, 0);
        editor.ScrollToCaret();
        editor.Focus();
        // نُتبع الإعلان بنص السطر الحالي ليعرف المستخدم أين استقر النص
        var lines = editor.Lines;
        int line = lines.Length == 0 ? -1 : Math.Min(LineFromIndex(text, pos), lines.Length - 1);
        Announce(line >= 0 && !IsBlankLine(lines[line]) ? $"{action}. {lines[line]}" : action);
    }

    // ---------- أدوات ----------

    /// <summary>يعلن الموضع الحالي: اسم الملاحظة، القسم، رقم السطر، وعدد الكلمات.</summary>
    void AnnounceWhereAmI()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        var text = editor.Text;
        var lines = editor.Lines;
        int line = LineFromIndex(text, editor.SelectionStart);

        string? section = null;
        for (int i = Math.Min(line, lines.Length - 1); i >= 0; i--)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith('#')) { section = t.TrimStart('#').Trim(); break; }
        }

        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var parts = new List<string> { L.T($"الملاحظة: {vault.DisplayName(currentNote)}", $"Note: {vault.DisplayName(currentNote)}") };
        if (section != null) parts.Add(L.T($"القسم: {section}", $"Section: {section}"));
        parts.Add(L.T($"السطر {line + 1} من {lines.Length}", $"Line {line + 1} of {lines.Length}"));
        parts.Add(L.T($"الكلمات: {words}", $"Words: {words}"));
        Announce(string.Join(L.T("، ", ", "), parts));
    }

    /// <summary>يعود إلى الملاحظة السابقة مباشرة — تنقّل ذهاب وإياب بين ملاحظتين تعمل عليهما.</summary>
    void ToggleLastNote()
    {
        var prev = settings.RecentNotes.FirstOrDefault(p =>
            !string.Equals(p, currentNote, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(p) &&
            p.StartsWith(vault.Root, StringComparison.OrdinalIgnoreCase));
        if (prev == null) { Announce(L.T("لا توجد ملاحظة سابقة", "No previous note")); return; }
        OpenNote(prev);
        editor.Focus();
    }

    void ShowRecent()
    {
        var recent = settings.RecentNotes
            .Where(p => File.Exists(p) && p.StartsWith(vault.Root, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (recent.Count == 0) { Announce(L.T("لا توجد ملاحظات حديثة", "No recent notes")); return; }
        using var dlg = new ListPickForm(L.T("الملاحظات الأخيرة", "Recent notes"), L.T("قائمة الملاحظات الأخيرة", "Recent notes list"));
        foreach (var p in recent)
            dlg.AddItem(vault.RelativeName(p), p);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is string path)
        {
            OpenNote(path);
            FocusEditorFresh();
        }
    }

    /// <summary>
    /// يدرج كتلة كود بأسوارها الثلاثية دون أن يكتب المستخدم علامة ` بنفسه —
    /// عسيرة على لوحة المفاتيح العربية وفوضوية وسط نص يميني الاتجاه.
    /// إن كان هناك نص محدد يُلَفّ داخل الكتلة مباشرة.
    /// </summary>
    void InsertCodeBlock()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        var lang = InputBox.Show(this,
            L.T("كتلة كود", "Code block"),
            L.T("لغة الكود (مثل python أو csharp — اتركها فارغة إن لم تهم):",
                "Code language (like python or csharp — leave empty if not needed):"),
            "", allowEmpty: true);
        if (lang == null) return; // أُلغي الحوار

        var text = editor.Text;
        int caret = editor.SelectionStart;
        var selected = editor.SelectedText;
        bool atLineStart = caret == 0 || (caret > 0 && caret <= text.Length && text[caret - 1] == '\n');
        var prefix = (atLineStart ? "" : "\r\n") + "```" + lang + "\r\n";

        if (selected.Length > 0)
        {
            var body = selected.EndsWith("\r\n") ? selected : selected + "\r\n";
            editor.SelectedText = prefix + body + "```\r\n";
            Announce(L.T("لُفّ النص المحدد داخل كتلة كود", "Wrapped the selection in a code block"));
        }
        else
        {
            editor.SelectedText = prefix + "\r\n```\r\n";
            // نعيد المؤشر إلى السطر الفارغ بين السورين ليكتب الكود مباشرة
            editor.Select(caret + prefix.Length, 0);
            Announce(L.T("أُدرجت كتلة كود، اكتب الكود الآن", "Code block inserted, type the code now"));
        }
        FocusEditorFresh();
    }

    void InsertTimestamp()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        var stamp = Settings.FormatTimestamp(settings.DateFormat, DateTime.Now);
        editor.SelectedText = stamp;
        editor.Focus();
        Announce(L.T($"أُدرج: {stamp}", $"Inserted: {stamp}"));
    }

    void CopyNote()
    {
        if (currentNote == null || editor.TextLength == 0) { Announce(L.T("لا يوجد نص للنسخ", "No text to copy")); return; }
        try
        {
            Clipboard.SetText(editor.Text);
            Announce(L.T("نُسخت الملاحظة كاملة إلى الحافظة", "Copied the entire note to the clipboard"));
        }
        catch { Announce(L.T("تعذر النسخ إلى الحافظة، أعد المحاولة", "Could not copy to the clipboard, try again")); }
    }

    /// <summary>
    /// يحوّل الملاحظة إلى HTML ويفتحها في المتصفح، حيث يقرأ NVDA العناوين والقوائم
    /// دلالياً، وكل قسم قابل للطي والتوسيع كما في Apple Notes.
    /// </summary>
    void PreviewHtml()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        SaveCurrent();
        var name = vault.DisplayName(currentNote);

        // روابط الويكي ليست جزءاً من Markdown القياسي، فنعرضها كنص بارز
        var md = Regex.Replace(editor.Text, @"\[\[([^\]\|#]+)(?:[#|][^\]]*)?\]\]", "**$1**");
        // DisableHtml: وسوم HTML خام داخل الملاحظات تُعرض نصاً ولا تُنفذ —
        // يمنع حقن سكربتات من ملاحظة واردة من قبو شخص آخر
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
        var body = MakeCollapsible(Markdown.ToHtml(md, pipeline));

        var dir1 = L.En ? "ltr" : "rtl";
        var lang = L.En ? "en" : "ar";
        var html = $$"""
            <!DOCTYPE html>
            <html dir="{{dir1}}" lang="{{lang}}">
            <head>
            <meta charset="utf-8">
            <title>{{WebUtility.HtmlEncode(name)}}</title>
            <style>
            body { font-family: 'Segoe UI', sans-serif; font-size: 18px; line-height: 1.9; max-width: 46em; margin: 2em auto; padding: 0 1em; color: #1f2937; }
            /* كل فقرة تختار اتجاهها تلقائياً من محتواها: العربية يميناً والإنجليزية يساراً */
            p, li, blockquote, h1, h2, h3, h4, h5, h6, td, th { unicode-bidi: plaintext; text-align: start; }
            code, pre { direction: ltr; font-size: 16px; }
            details { margin: 0.4em 0; }
            summary { cursor: pointer; }
            summary h1, summary h2, summary h3, summary h4, summary h5, summary h6 { display: inline; }
            </style>
            </head>
            <body>
            <main>
            {{body}}
            </main>
            </body>
            </html>
            """;

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Daftari");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Vault.Sanitize(name) + ".html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            Announce(L.T("فُتحت المعاينة في المتصفح", "Preview opened in the browser"));
        }
        catch (Exception ex) { Msg(L.T("تعذر فتح المعاينة: ", "Could not open the preview: ") + ex.Message); }
    }

    /// <summary>
    /// يلفّ كل عنوان ومحتواه داخل عنصر details قابل للطي (مفتوح افتراضياً)،
    /// وينهي كل قسم عند أول عنوان بنفس المستوى أو أعلى — كما في Apple Notes.
    /// </summary>
    static string MakeCollapsible(string body)
    {
        var rx = new Regex(@"<h([1-6])[^>]*>.*?</h\1>", RegexOptions.Singleline);
        var result = new StringBuilder();
        var stack = new Stack<int>();
        int pos = 0;
        foreach (Match m in rx.Matches(body))
        {
            result.Append(body, pos, m.Index - pos);
            int level = m.Value[2] - '0';
            while (stack.Count > 0 && stack.Peek() >= level)
            {
                result.Append("</details>");
                stack.Pop();
            }
            if (level == 1)
            {
                // العنوان الرئيسي لا يُطوى: محتواه يبقى ظاهراً دائماً
                result.Append(m.Value);
            }
            else
            {
                // الأقسام (مستوى 2) تبدأ مفتوحة، والتفاصيل العميقة (3 فأكثر) مطوية
                result.Append(level == 2 ? "<details open><summary>" : "<details><summary>")
                      .Append(m.Value).Append("</summary>");
                stack.Push(level);
            }
            pos = m.Index + m.Length;
        }
        result.Append(body, pos, body.Length - pos);
        while (stack.Count > 0)
        {
            result.Append("</details>");
            stack.Pop();
        }
        return result.ToString();
    }

    // ---------- الجداول ----------

    /// <summary>
    /// إنشاء جدول جديد أو تحرير الجدول الواقف عليه المؤشر عبر محرر شبكي يقرؤه NVDA،
    /// دون أن يكتب المستخدم أي رمز من رموز جداول Markdown بنفسه.
    /// </summary>
    void EditTable()
    {
        if (currentNote == null) { Announce(L.T("لا توجد ملاحظة مفتوحة", "No note is open")); return; }
        var text = editor.Text;
        var lines = editor.Lines;
        int caretLine = lines.Length == 0 ? 0 : Math.Min(LineFromIndex(text, editor.SelectionStart), lines.Length - 1);

        if (lines.Length > 0 && TryParseTable(lines, caretLine, out int start, out int end, out var headers, out var rows))
        {
            using var dlg = new TableEditorForm(headers, rows);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var md = SerializeTable(dlg.ResultHeaders, dlg.ResultRows);
            int startIdx = LineStartIndex(text, start);
            int endIdx = end + 1 < lines.Length ? LineStartIndex(text, end + 1) : text.Length;
            editor.Select(startIdx, endIdx - startIdx);
            editor.SelectedText = md;
            editor.Select(startIdx, 0);
            FocusEditorFresh();
            Announce(L.T($"حُدّث الجدول: {dlg.ResultRows.Count} صف و{dlg.ResultHeaders.Length} عمود",
                         $"Table updated: {dlg.ResultRows.Count} rows and {dlg.ResultHeaders.Length} columns"));
        }
        else
        {
            using var size = new TableSizeForm();
            if (size.ShowDialog(this) != DialogResult.OK) return;
            var newHeaders = Enumerable.Range(1, size.ChosenColumns)
                .Select(i => L.T($"عمود {i}", $"Column {i}")).ToArray();
            var newRows = Enumerable.Range(0, size.ChosenRows)
                .Select(_ => new string[size.ChosenColumns]).ToList();
            using var dlg = new TableEditorForm(newHeaders, newRows);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var md = SerializeTable(dlg.ResultHeaders, dlg.ResultRows);
            int caret = editor.SelectionStart;
            bool atLineStart = caret == 0 || (caret <= text.Length && caret > 0 && text[caret - 1] == '\n');
            editor.SelectedText = (atLineStart ? "" : "\r\n") + md;
            FocusEditorFresh();
            Announce(L.T($"أُدرج جدول: {dlg.ResultRows.Count} صف و{dlg.ResultHeaders.Length} عمود",
                         $"Table inserted: {dlg.ResultRows.Count} rows and {dlg.ResultHeaders.Length} columns"));
        }
    }

    static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    /// <summary>هل هذا صف الفواصل في جدول Markdown مثل | --- | :--- |؟</summary>
    static bool IsSeparatorRow(string line)
    {
        if (!IsTableLine(line)) return false;
        var cells = SplitTableRow(line);
        return cells.Any(c => c.Contains('-')) &&
               cells.All(c => c.Length == 0 || Regex.IsMatch(c, @"^:?-+:?$"));
    }

    /// <summary>يفصل خلايا صف جدول Markdown مع مراعاة العمود المهرّب بشرطة مائلة.</summary>
    static string[] SplitTableRow(string line)
    {
        var t = line.Trim().Replace("\\|", "\u0001");
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').Select(c => c.Replace("\u0001", "|").Trim()).ToArray();
    }

    /// <summary>يتعرف على كتلة الجدول المحيطة بسطر المؤشر ويفكك رؤوسها وصفوفها.</summary>
    static bool TryParseTable(string[] lines, int caretLine, out int start, out int end,
        out string[] headers, out List<string[]> rows)
    {
        headers = Array.Empty<string>();
        rows = new List<string[]>();
        start = end = -1;
        if (!IsTableLine(lines[caretLine])) return false;

        start = caretLine;
        while (start > 0 && IsTableLine(lines[start - 1])) start--;
        end = caretLine;
        while (end + 1 < lines.Length && IsTableLine(lines[end + 1])) end++;

        var block = new List<string[]>();
        for (int i = start; i <= end; i++) block.Add(SplitTableRow(lines[i]));

        headers = block[0];
        // جدول برؤوس أقل من خلايا صفوفه: نكمل الرؤوس كي لا تضيع الخلايا الزائدة عند التحرير
        int width = block.Max(b => b.Length);
        if (headers.Length < width)
            headers = headers.Concat(Enumerable.Range(headers.Length + 1, width - headers.Length)
                .Select(i => L.T($"عمود {i}", $"Column {i}"))).ToArray();
        int dataStart = block.Count > 1 && IsSeparatorRow(lines[start + 1]) ? 2 : 1;
        for (int i = dataStart; i < block.Count; i++) rows.Add(block[i]);
        return true;
    }

    /// <summary>يكتب الجدول بصيغة Markdown الصحيحة: رؤوس، صف فاصل، ثم الصفوف.</summary>
    static string SerializeTable(string[] headers, List<string[]> rows)
    {
        static string Esc(string? s) =>
            (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|").Trim();

        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers.Select(Esc))).Append(" |\r\n");
        sb.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", headers.Length))).Append("\r\n");
        foreach (var r in rows)
            sb.Append("| ")
              .Append(string.Join(" | ", headers.Select((_, i) => Esc(i < r.Length ? r[i] : ""))))
              .Append(" |\r\n");
        return sb.ToString();
    }

    // ---------- النسخ الاحتياطي والإعدادات ----------

    void BackupNow()
    {
        SaveCurrent();
        if (string.IsNullOrWhiteSpace(settings.BackupFolder))
        {
            Msg(L.T("حدد مجلد النسخ الاحتياطي أولاً من الإعدادات. اختر مجلداً داخل Google Drive أو OneDrive لرفع النسخ إلى السحابة تلقائياً.",
                    "Set the backup folder in Settings first. Choose a folder inside Google Drive or OneDrive to sync backups to the cloud automatically."));
            OpenSettings();
            if (string.IsNullOrWhiteSpace(settings.BackupFolder)) return;
        }
        // مجلد نسخ داخل القبو نفسه يجعل الضغط يحاول ضم الملف الذي يكتبه فيفشل،
        // وتتراكم النسخ القديمة داخل كل نسخة جديدة
        var backupFull = Path.GetFullPath(settings.BackupFolder!);
        var vaultFull = Path.GetFullPath(vault.Root);
        if (string.Equals(backupFull, vaultFull, StringComparison.OrdinalIgnoreCase) ||
            backupFull.StartsWith(vaultFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Msg(L.T("مجلد النسخ الاحتياطي لا يصح أن يكون داخل مجلد القبو نفسه. اختر مجلداً خارجه من الإعدادات.",
                    "The backup folder cannot be inside the vault itself. Choose a folder outside it in Settings."));
            return;
        }
        try
        {
            Directory.CreateDirectory(settings.BackupFolder!);
            var name = $"Daftari-{Path.GetFileName(vault.Root)}-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";
            var dest = Path.Combine(settings.BackupFolder!, name);
            if (File.Exists(dest)) File.Delete(dest);
            ZipFile.CreateFromDirectory(vault.Root, dest, CompressionLevel.Optimal, includeBaseDirectory: false);
            Announce(L.T($"تم إنشاء النسخة الاحتياطية: {name}", $"Backup created: {name}"));
        }
        catch (Exception ex) { Msg(L.T("تعذر إنشاء النسخة الاحتياطية: ", "Could not create the backup: ") + ex.Message); }
    }

    void OpenSettings()
    {
        using var dlg = new SettingsForm(settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.LanguageChanged)
        {
            var answer = MessageBox.Show(this,
                L.T("تغيّرت لغة التطبيق. هل تريد إعادة التشغيل الآن لتطبيقها؟",
                    "The application language changed. Restart now to apply it?"),
                L.T("إعادة التشغيل", "Restart"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1, L.MsgOptions);
            if (answer == DialogResult.Yes)
            {
                SaveCurrent();
                settings.Save();
                Application.Restart();
                return;
            }
        }
        Announce(L.T("حُفظت الإعدادات", "Settings saved"));
    }

    // ---------- العرض والإعلانات ----------

    void ToggleDirection()
    {
        bool rtl = editor.RightToLeft != RightToLeft.Yes;
        editor.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        settings.EditorRightToLeft = rtl;
        settings.Save();
        Announce(rtl
            ? L.T("اتجاه النص: من اليمين إلى اليسار", "Text direction: right to left")
            : L.T("اتجاه النص: من اليسار إلى اليمين", "Text direction: left to right"));
    }

    /// <summary>
    /// مع الالتفاف يُقسم السطر الطويل بصرياً على عرض النافذة فيقرؤه NVDA قطعاً قصيرة؛
    /// تعطيله يجعل كل سطر منطقي يُقرأ كاملاً مهما طال.
    /// </summary>
    void ToggleWrap()
    {
        int caret = editor.SelectionStart; // تغيير الالتفاف يعيد إنشاء الحقل داخلياً
        settings.WordWrap = !settings.WordWrap;
        editor.WordWrap = settings.WordWrap;
        editor.ScrollBars = settings.WordWrap ? ScrollBars.Vertical : ScrollBars.Both;
        editor.Select(Math.Min(caret, editor.TextLength), 0);
        settings.Save();
        Announce(settings.WordWrap
            ? L.T("التفاف الأسطر مفعّل: السطر الطويل يُقسم على عرض النافذة", "Word wrap on: long lines split at the window width")
            : L.T("التفاف الأسطر معطّل: كل سطر يُقرأ كاملاً مهما طال", "Word wrap off: every line is read in full"));
    }

    void ChangeFont(int delta)
    {
        var old = editor.Font;
        var size = Math.Clamp(old.Size + delta, 8f, 40f);
        editor.Font = new Font(old.FontFamily, size);
        old.Dispose();
        settings.FontSize = size;
        settings.Save();
        Announce(L.T($"حجم الخط {size}", $"Font size {size}"));
    }

    void UpdateCount()
    {
        var text = editor.Text;
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        countLabel.Text = L.T($"الكلمات: {words} | الأحرف: {text.Length}", $"Words: {words} | Characters: {text.Length}");
    }

    /// <summary>
    /// إعلان نصي يقرؤه NVDA عبر إشعارات UI Automation، مع عرضه في شريط الحالة.
    /// interrupt=false يجعل الإعلان ينتظر انتهاء كلام NVDA الجاري (مثل إعلان انتقال التركيز) بدل مقاطعته.
    /// </summary>
    void Announce(string message, bool interrupt = true)
    {
        statusLabel.Text = message;
        // الإشعار يصدر من العنصر الذي عليه التركيز ليضمن NVDA التقاطه وقراءته
        Control source = this;
        Control? c = ActiveControl;
        while (c != null) { source = c; c = (c as ContainerControl)?.ActiveControl; }
        try
        {
            source.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.ActionCompleted,
                interrupt ? AutomationNotificationProcessing.MostRecent : AutomationNotificationProcessing.All,
                message);
        }
        catch { }
    }

    void Msg(string message) =>
        MessageBox.Show(this, message, L.T("دفتري", "Daftari"), MessageBoxButtons.OK, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1, L.MsgOptions);

    // ---------- نصوص ----------

    static string HelpText => L.En ? HelpTextEn : HelpTextAr;
    static string WelcomeText => L.En ? WelcomeTextEn : WelcomeTextAr;

    const string HelpTextAr =
"""
اختصارات دفتري

الملفات:
Ctrl+N — ملاحظة جديدة
Ctrl+Shift+N — مجلد جديد
F2 — إعادة تسمية العنصر المحدد
Delete (في الشجرة) — نقل إلى المحذوفات
Ctrl+S — حفظ (الحفظ يتم تلقائياً أيضاً)
F5 — تحديث شجرة الملاحظات
Ctrl+O — فتح قبو آخر
Ctrl+Shift+B — نسخة احتياطية الآن
Ctrl+, — الإعدادات (اللغة، تنسيق التاريخ، مجلد النسخ الاحتياطي)

التنقل:
Ctrl+P — فتح ملاحظة بسرعة بالاسم
Ctrl+Tab — العودة إلى الملاحظة السابقة (ذهاب وإياب)
Ctrl+R — الملاحظات الأخيرة
Ctrl+I — أين أنا؟ (اسم الملاحظة والقسم والسطر وعدد الكلمات)
Ctrl+E — الانتقال إلى المحرر
Ctrl+Shift+E — الانتقال إلى شجرة الملاحظات
F6 — التبديل بين الشجرة والمحرر
Ctrl+سهم لأعلى أو لأسفل — التنقل بين الفقرات
Alt+سهم لأعلى أو لأسفل — القفز بين العناوين
Ctrl+J — قائمة عناوين الملاحظة الحالية
Ctrl+D — ملاحظة اليوم (تُنشأ في مجلد اليوميات)

الروابط:
اكتب [[اسم الملاحظة]] لإنشاء رابط.
Ctrl+Enter — اتباع الرابط عند مؤشر الكتابة
Ctrl+K — إدراج رابط عبر البحث عن ملاحظة
Ctrl+B — عرض الروابط الواردة إلى الملاحظة الحالية

البحث:
Ctrl+F — بحث داخل الملاحظة، ثم F3 للتالي
Ctrl+Shift+F — البحث في كل ملاحظات القبو
Ctrl+T — استعراض الوسوم (اكتب #وسم في أي ملاحظة)

التحرير:
Ctrl+Z — تراجع
Ctrl+Y أو Ctrl+Shift+Z — إعادة
Ctrl+Shift+سهم لأعلى أو لأسفل — تحديد حتى حدود الفقرة
Ctrl+Shift+G — جدول: إنشاء جدول جديد، أو تحرير الجدول الواقف عليه المؤشر
(تحرر الخلايا في شبكة يقرؤها NVDA صفاً وعموداً كما في Excel،
والتطبيق يكتب رموز Markdown بنفسه — لا حاجة لكتابة أي عود |،
ولقراءة الجداول بأريح طريقة افتحها بـ Ctrl+Shift+H في المتصفح
وتنقل بين الخلايا بأوامر الجداول Ctrl+Alt+الأسهم)
Ctrl+Shift+K — إدراج كتلة كود برمجي (يكتب الأسوار ``` بنفسه،
وإن كان هناك نص محدد يلفّه داخل الكتلة مباشرة)
Ctrl+Shift+T — إدراج التاريخ والوقت الحاليين
Ctrl+Shift+C — نسخ الملاحظة كاملة إلى الحافظة
Ctrl+Shift+H — فتح الملاحظة بصيغة HTML في المتصفح
(في المتصفح يقرأ NVDA العناوين والقوائم دلالياً بدون رموز،
وكل قسم قابل للطي والتوسيع بضغط Enter على عنوانه؛
العنوان الرئيسي لا يُطوى، والأقسام مستوى 2 تبدأ مفتوحة،
والتفاصيل العميقة مستوى 3 فأكثر تبدأ مطوية داخل أقسامها،
وتتنقل بين العناوين بحرف H كأي صفحة ويب)

العرض:
Ctrl+Shift+D — تبديل اتجاه النص في المحرر
Ctrl+Shift+W — تبديل التفاف الأسطر
(مع الالتفاف يقرأ NVDA السطر الطويل قطعاً بعرض النافذة،
وبدونه يقرأ كل سطر كاملاً مهما طال)
Ctrl+= و Ctrl+- — تكبير وتصغير الخط

ملاحظة: الملاحظات ملفات Markdown عادية في مجلد القبو،
ويمكن فتح نفس المجلد ببرنامج Obsidian أو أي محرر آخر.
""";

    const string HelpTextEn =
"""
Daftari shortcuts

Files:
Ctrl+N — new note
Ctrl+Shift+N — new folder
F2 — rename selected item
Delete (in the tree) — move to trash
Ctrl+S — save (autosave also runs)
F5 — refresh the notes tree
Ctrl+O — open another vault
Ctrl+Shift+B — back up now
Ctrl+, — settings (language, date format, backup folder)

Navigation:
Ctrl+P — quick open a note by name
Ctrl+Tab — switch back to the previous note
Ctrl+R — recent notes
Ctrl+I — where am I? (note, section, line, word count)
Ctrl+E — go to the editor
Ctrl+Shift+E — go to the notes tree
F6 — switch between tree and editor
Ctrl+Up or Down arrow — move between paragraphs
Alt+Up or Down arrow — jump between headings
Ctrl+J — list of headings in the current note
Ctrl+D — today's note (created in the journal folder)

Links:
Write [[note name]] to create a link.
Ctrl+Enter — follow the link at the caret
Ctrl+K — insert a link by searching for a note
Ctrl+B — show backlinks to the current note

Search:
Ctrl+F — find in note, then F3 for next
Ctrl+Shift+F — search all notes in the vault
Ctrl+T — browse tags (write #tag in any note)

Editing:
Ctrl+Z — undo
Ctrl+Y or Ctrl+Shift+Z — redo
Ctrl+Shift+Up or Down arrow — select to paragraph boundary
Ctrl+Shift+G — table: create a new table, or edit the one at the caret
(you edit cells in a grid NVDA reads row by column like Excel,
and the app writes the Markdown syntax itself — no pipes needed;
to read tables comfortably open them with Ctrl+Shift+H in the browser
and move between cells with the table commands Ctrl+Alt+arrows)
Ctrl+Shift+K — insert a code block (writes the ``` fences itself,
and wraps the selection if text is selected)
Ctrl+Shift+T — insert current date and time
Ctrl+Shift+C — copy the entire note
Ctrl+Shift+H — open the note as HTML in the browser
(NVDA reads headings and lists semantically there,
each section collapses and expands with Enter on its heading;
the main title never collapses, level-2 sections start open,
and deep sections of level 3+ start collapsed inside their parents,
and you can jump between headings with the H key)

View:
Ctrl+Shift+D — toggle editor text direction
Ctrl+Shift+W — toggle word wrap
(with wrap on NVDA reads long lines in window-width chunks,
with wrap off every line is read in full)
Ctrl+= and Ctrl+- — increase and decrease font size

Note: notes are plain Markdown files in the vault folder,
and the same folder can be opened with Obsidian or any editor.
""";

    const string WelcomeTextAr =
"""
# أهلاً بك في دفتري

دفتري تطبيق ملاحظات عربي صُمم ليعمل بسلاسة مع قارئ الشاشة NVDA.

كل ملاحظة هي ملف Markdown عادي داخل مجلد القبو، لذا ملاحظاتك ملكك دائماً ويمكن فتحها بأي برنامج آخر.

## أساسيات سريعة

- اضغط Ctrl+N لإنشاء ملاحظة جديدة.
- اضغط Ctrl+P لفتح أي ملاحظة بالاسم بسرعة.
- اضغط F6 للتنقل بين شجرة الملاحظات والمحرر.
- الحفظ تلقائي، وCtrl+S متاح دائماً.

## الروابط بين الملاحظات

اكتب اسم ملاحظة بين قوسين مربعين مزدوجين لإنشاء رابط، مثل: [[أفكار المشروع]]
ثم ضع المؤشر داخل الرابط واضغط Ctrl+Enter لفتحها (وستُنشأ إن لم تكن موجودة).
اضغط Ctrl+B في أي ملاحظة لمعرفة الملاحظات التي تشير إليها.

## الوسوم

اكتب #وسم في أي مكان، مثل: #مهم أو #عمل
ثم اضغط Ctrl+T لاستعراض كل الوسوم والملاحظات الحاوية لها.

اضغط F1 في أي وقت لعرض قائمة الاختصارات كاملة.

#ترحيب
""";

    const string WelcomeTextEn =
"""
# Welcome to Daftari

Daftari is a note-taking app designed to work smoothly with the NVDA screen reader.

Every note is a plain Markdown file inside the vault folder, so your notes are always yours and can be opened with any other program.

## Quick basics

- Press Ctrl+N to create a new note.
- Press Ctrl+P to quickly open any note by name.
- Press F6 to switch between the notes tree and the editor.
- Saving is automatic, and Ctrl+S is always available.

## Links between notes

Write a note name between double square brackets to create a link, like: [[Project ideas]]
Then place the caret inside the link and press Ctrl+Enter to open it (it is created if missing).
Press Ctrl+B in any note to see which notes point to it.

## Tags

Write #tag anywhere, like: #important or #work
Then press Ctrl+T to browse all tags and the notes containing them.

Press F1 at any time to show the full shortcut list.

#welcome
""";
}
