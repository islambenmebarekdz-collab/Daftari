using System.Windows.Forms.Automation;

namespace Daftari;

/// <summary>حوار إدخال نص بسيط، مُسمّى بالكامل لقارئ الشاشة.</summary>
public static class InputBox
{
    public static string? Show(IWin32Window owner, string title, string prompt, string initial = "", bool allowEmpty = false)
    {
        using var f = new Form
        {
            Text = title,
            RightToLeft = L.Rtl,
            RightToLeftLayout = L.RtlLayout,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(440, 130)
        };
        var lbl = new Label { Text = prompt, Left = 12, Top = 12, Width = 416 };
        var tb = new TextBox { Left = 12, Top = 40, Width = 416, Text = initial, AccessibleName = prompt };
        var ok = new Button { Text = L.T("موافق", "OK"), DialogResult = DialogResult.OK, Left = 12, Top = 84, Width = 110 };
        var cancel = new Button { Text = L.T("إلغاء", "Cancel"), DialogResult = DialogResult.Cancel, Left = 130, Top = 84, Width = 110 };
        f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        tb.SelectAll();
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        var result = tb.Text.Trim();
        return result.Length > 0 || allowEmpty ? result : null;
    }
}

/// <summary>
/// قائمة اختيار عامة: عنصر واحد لكل نتيجة، Enter للاختيار، Escape للإغلاق.
/// </summary>
public class ListPickForm : Form
{
    readonly ListBox list = new();
    readonly List<object?> payloads = new();
    public object? Result { get; private set; }

    public ListPickForm(string title, string accessibleName)
    {
        Text = title;
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(640, 420);
        KeyPreview = true;

        list.Dock = DockStyle.Fill;
        list.AccessibleName = accessibleName;
        list.IntegralHeight = false;
        Controls.Add(list);

        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
        };
        list.DoubleClick += (_, _) => Accept();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
        Shown += (_, _) => { if (list.Items.Count > 0) list.SelectedIndex = 0; list.Focus(); };
    }

    public void AddItem(string display, object? payload)
    {
        list.Items.Add(display);
        payloads.Add(payload);
    }

    public int Count => list.Items.Count;

    void Accept()
    {
        if (list.SelectedIndex < 0) return;
        Result = payloads[list.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// منتقي ملاحظات مع حقل ترشيح: للفتح السريع وإدراج الروابط.
/// السهم لأسفل ينقل من حقل الترشيح إلى القائمة، وEnter يختار.
/// </summary>
public class NotePickerForm : Form
{
    readonly TextBox filter = new();
    readonly ListBox list = new();
    readonly List<(string Display, string Path)> all;
    readonly List<string?> payloads = new();
    readonly bool allowCreate;

    public string? SelectedPath { get; private set; }
    public string? CreateName { get; private set; }

    public NotePickerForm(string title, string prompt, IEnumerable<(string Display, string Path)> items, bool allowCreate)
    {
        this.allowCreate = allowCreate;
        all = items.OrderBy(x => x.Display, StringComparer.CurrentCultureIgnoreCase).ToList();

        Text = title;
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 440);
        KeyPreview = true;

        var lbl = new Label { Text = prompt, Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 8, 0) };
        filter.Dock = DockStyle.Top;
        filter.AccessibleName = prompt;
        list.Dock = DockStyle.Fill;
        list.AccessibleName = L.T("النتائج", "Results");
        list.IntegralHeight = false;

        Controls.Add(list);
        Controls.Add(filter);
        Controls.Add(lbl);

        filter.TextChanged += (_, _) => Refill();
        filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && list.Items.Count > 0)
            {
                list.Focus();
                if (list.SelectedIndex < 0) list.SelectedIndex = 0;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
        };
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
        };
        list.DoubleClick += (_, _) => Accept();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };

        Refill();
    }

    void Refill()
    {
        var q = filter.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        payloads.Clear();
        foreach (var (display, path) in all)
        {
            if (q.Length == 0 || display.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            {
                list.Items.Add(display);
                payloads.Add(path);
                if (list.Items.Count >= 300) break;
            }
        }
        if (list.Items.Count == 0 && allowCreate && q.Length > 0)
        {
            list.Items.Add(L.T($"إنشاء ملاحظة جديدة باسم: {q}", $"Create a new note named: {q}"));
            payloads.Add(null);
        }
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        list.EndUpdate();
    }

    void Accept()
    {
        int idx = list.SelectedIndex >= 0 ? list.SelectedIndex : (list.Items.Count > 0 ? 0 : -1);
        if (idx < 0) return;
        if (payloads[idx] is null)
        {
            CreateName = filter.Text.Trim();
            if (CreateName.Length == 0) return;
        }
        else SelectedPath = payloads[idx];
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>البحث في كل ملاحظات القبو: حقل استعلام وقائمة نتائج.</summary>
public class VaultSearchForm : Form
{
    readonly TextBox query = new();
    readonly ListBox results = new();
    readonly List<SearchHit> hits = new();
    readonly Vault vault;

    public SearchHit? Selected { get; private set; }

    public VaultSearchForm(Vault vault)
    {
        this.vault = vault;
        Text = L.T("البحث في كل الملاحظات", "Search all notes");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 460);
        KeyPreview = true;

        var lbl = new Label
        {
            Text = L.T("نص البحث (اضغط Enter للبحث):", "Search text (press Enter to search):"),
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(8, 6, 8, 0)
        };
        query.Dock = DockStyle.Top;
        query.AccessibleName = L.T("نص البحث", "Search text");
        results.Dock = DockStyle.Fill;
        results.AccessibleName = L.T("نتائج البحث", "Search results");
        results.IntegralHeight = false;

        Controls.Add(results);
        Controls.Add(query);
        Controls.Add(lbl);

        query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DoSearch(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down && results.Items.Count > 0)
            {
                results.Focus();
                if (results.SelectedIndex < 0) results.SelectedIndex = 0;
                e.SuppressKeyPress = true;
            }
        };
        results.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
        };
        results.DoubleClick += (_, _) => Accept();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
    }

    void DoSearch()
    {
        var q = query.Text.Trim();
        if (q.Length == 0) return;
        results.BeginUpdate();
        results.Items.Clear();
        hits.Clear();
        foreach (var hit in vault.Search(q))
        {
            hits.Add(hit);
            var snippet = hit.LineText.Length > 80 ? hit.LineText[..80] + "…" : hit.LineText;
            results.Items.Add(L.T(
                $"{vault.RelativeName(hit.FilePath)} — السطر {hit.LineNumber + 1}: {snippet}",
                $"{vault.RelativeName(hit.FilePath)} — line {hit.LineNumber + 1}: {snippet}"));
            if (hits.Count >= 500) break;
        }
        results.EndUpdate();
        if (results.Items.Count > 0)
        {
            results.SelectedIndex = 0;
            results.Focus();
        }
        else
        {
            results.Items.Add(L.T("لا توجد نتائج", "No results"));
            hits.Clear();
        }
    }

    void Accept()
    {
        int idx = results.SelectedIndex;
        if (idx < 0 || idx >= hits.Count) return;
        Selected = hits[idx];
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>الوسوم على مرحلتين: قائمة الوسوم ثم قائمة الملاحظات الحاوية للوسم المختار.</summary>
public class TagsForm : Form
{
    readonly ListBox list = new();
    readonly Label header = new();
    readonly SortedDictionary<string, List<string>> tags;
    readonly Vault vault;
    List<string>? currentNotes;

    public string? SelectedNote { get; private set; }

    public TagsForm(Vault vault, SortedDictionary<string, List<string>> tags)
    {
        this.vault = vault;
        this.tags = tags;
        Text = L.T("الوسوم", "Tags");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 440);
        KeyPreview = true;

        header.Dock = DockStyle.Top;
        header.Height = 28;
        header.Padding = new Padding(8, 6, 8, 0);
        list.Dock = DockStyle.Fill;
        list.IntegralHeight = false;
        Controls.Add(list);
        Controls.Add(header);

        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
        };
        list.DoubleClick += (_, _) => Accept();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (currentNotes != null) ShowTags();
                else { DialogResult = DialogResult.Cancel; Close(); }
                e.SuppressKeyPress = true;
            }
        };

        ShowTags();
        Shown += (_, _) => list.Focus();
    }

    void ShowTags()
    {
        currentNotes = null;
        header.Text = L.T("اختر وسماً (Enter لعرض ملاحظاته):", "Choose a tag (Enter shows its notes):");
        list.AccessibleName = L.T("قائمة الوسوم", "Tag list");
        list.Items.Clear();
        foreach (var kv in tags)
            list.Items.Add($"#{kv.Key} ({kv.Value.Count})");
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    void Accept()
    {
        if (list.SelectedIndex < 0) return;
        if (currentNotes == null)
        {
            var tag = tags.Keys.ElementAt(list.SelectedIndex);
            currentNotes = tags[tag];
            header.Text = L.T($"ملاحظات الوسم #{tag} (Escape للعودة إلى الوسوم):",
                              $"Notes tagged #{tag} (Escape returns to tags):");
            list.AccessibleName = L.T($"ملاحظات الوسم {tag}", $"Notes tagged {tag}");
            list.Items.Clear();
            foreach (var p in currentNotes)
                list.Items.Add(vault.RelativeName(p));
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        else
        {
            SelectedNote = currentNotes[list.SelectedIndex];
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

/// <summary>نافذة الاختصارات: نص للقراءة فقط يتنقل فيه قارئ الشاشة بالأسهر.</summary>
public class HelpForm : Form
{
    public HelpForm(string text)
    {
        Text = L.T("اختصارات دفتري", "Daftari shortcuts");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 500);
        KeyPreview = true;

        var tb = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = text,
            AccessibleName = L.T("قائمة الاختصارات", "Shortcut list"),
            TabStop = true
        };
        Controls.Add(tb);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };
        Shown += (_, _) => { tb.SelectionStart = 0; tb.SelectionLength = 0; tb.Focus(); };
    }
}

/// <summary>يسأل عن أبعاد الجدول الجديد قبل فتح محرر الشبكة.</summary>
public class TableSizeForm : Form
{
    readonly NumericUpDown cols = new() { Minimum = 1, Maximum = 12, Value = 3 };
    readonly NumericUpDown rows = new() { Minimum = 1, Maximum = 100, Value = 3 };

    public int ChosenColumns => (int)cols.Value;
    public int ChosenRows => (int)rows.Value;

    public TableSizeForm()
    {
        Text = L.T("جدول جديد", "New table");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 180);

        var lblC = new Label { Text = L.T("عدد الأعمدة:", "Number of columns:"), Left = 16, Top = 18, Width = 200 };
        cols.SetBounds(220, 16, 100, 30);
        cols.AccessibleName = L.T("عدد الأعمدة", "Number of columns");
        var lblR = new Label { Text = L.T("عدد الصفوف:", "Number of rows:"), Left = 16, Top = 62, Width = 200 };
        rows.SetBounds(220, 60, 100, 30);
        rows.AccessibleName = L.T("عدد الصفوف", "Number of rows");

        var ok = new Button { Text = L.T("متابعة", "Continue"), DialogResult = DialogResult.OK, Width = 110 };
        ok.SetBounds(16, 120, 110, 34);
        var cancel = new Button { Text = L.T("إلغاء", "Cancel"), DialogResult = DialogResult.Cancel, Width = 110 };
        cancel.SetBounds(134, 120, 110, 34);

        Controls.AddRange(new Control[] { lblC, cols, lblR, rows, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

/// <summary>
/// محرر جداول شبكي: يحرر المستخدم الخلايا في شبكة حقيقية يقرؤها NVDA
/// صفاً وعموداً كما في Excel، والتطبيق يتولى صياغة Markdown بنفسه.
/// </summary>
public class TableEditorForm : Form
{
    readonly DataGridView grid = new();
    int columnCounter;

    public string[] ResultHeaders { get; private set; } = Array.Empty<string>();
    public List<string[]> ResultRows { get; } = new();

    public TableEditorForm(string[] headers, List<string[]> rows)
    {
        Text = L.T("محرر الجدول", "Table editor");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(820, 520);

        grid.Dock = DockStyle.Fill;
        grid.AccessibleName = L.T("خلايا الجدول", "Table cells");
        // لا صف إدخال وهمي: كان يظهر كصف زائد يقرؤه NVDA بالإنجليزية "row"
        // ويضيف صفوفاً غير مقصودة عند الكتابة — الإضافة بالزر فقط
        grid.AllowUserToAddRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.RowHeadersWidth = 90;

        foreach (var h in headers) AddColumn(h);
        foreach (var r in rows)
        {
            int idx = grid.Rows.Add();
            for (int c = 0; c < grid.Columns.Count && c < r.Length; c++)
                grid.Rows[idx].Cells[c].Value = r[c] ?? "";
        }
        RenumberRows();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(8, 6, 8, 6)
        };
        Button B(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            b.Click += onClick;
            buttons.Controls.Add(b);
            return b;
        }
        B(L.T("إضافة صف", "Add row"), (_, _) =>
        {
            int idx = grid.Rows.Add();
            RenumberRows();
            grid.CurrentCell = grid.Rows[idx].Cells[0];
            grid.Focus();
            Say(L.T($"أُضيف الصف {idx + 1}", $"Added row {idx + 1}"));
        });
        B(L.T("حذف الصف الحالي", "Delete current row"), (_, _) => DeleteRow());
        B(L.T("إضافة عمود...", "Add column..."), (_, _) => AddColumnPrompt());
        B(L.T("إعادة تسمية العمود...", "Rename column..."), (_, _) => RenameColumn());
        B(L.T("حذف العمود الحالي", "Delete current column"), (_, _) => DeleteColumn());

        var ok = new Button { Text = L.T("إدراج في الملاحظة", "Insert into note"), DialogResult = DialogResult.OK, AutoSize = true };
        ok.Click += (_, _) => Collect();
        var cancel = new Button { Text = L.T("إلغاء", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(grid);
        Controls.Add(buttons);
        CancelButton = cancel; // لا AcceptButton: مفتاح Enter مخصص للتنقل داخل الشبكة

        Shown += (_, _) => grid.Focus();
    }

    void AddColumn(string header)
    {
        int idx = grid.Columns.Add($"c{columnCounter++}", header);
        grid.Columns[idx].SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    void AddColumnPrompt()
    {
        var name = InputBox.Show(this, L.T("إضافة عمود", "Add column"), L.T("عنوان العمود الجديد:", "New column header:"));
        if (name == null) return;
        AddColumn(name);
        Say(L.T($"أُضيف العمود {name}", $"Added column {name}"));
    }

    void RenameColumn()
    {
        if (grid.CurrentCell == null) return;
        var col = grid.Columns[grid.CurrentCell.ColumnIndex];
        var name = InputBox.Show(this, L.T("إعادة تسمية العمود", "Rename column"), L.T("العنوان الجديد:", "New header:"), col.HeaderText);
        if (name == null) return;
        col.HeaderText = name;
        Say(L.T($"صار عنوان العمود {name}", $"Column renamed to {name}"));
    }

    void DeleteColumn()
    {
        if (grid.CurrentCell == null) return;
        if (grid.Columns.Count <= 1) { Say(L.T("لا يمكن حذف العمود الأخير", "Cannot delete the last column")); return; }
        var col = grid.Columns[grid.CurrentCell.ColumnIndex];
        grid.Columns.Remove(col);
        Say(L.T("حُذف العمود", "Column deleted"));
    }

    void DeleteRow()
    {
        if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow)
        {
            Say(L.T("لا يوجد صف لحذفه", "No row to delete"));
            return;
        }
        grid.Rows.Remove(grid.CurrentRow);
        RenumberRows();
        Say(L.T("حُذف الصف", "Row deleted"));
    }

    /// <summary>يرقّم رؤوس الصفوف بالعربية كي يعلنها NVDA بدل "Row" الإنجليزية.</summary>
    void RenumberRows()
    {
        for (int i = 0; i < grid.Rows.Count; i++)
            grid.Rows[i].HeaderCell.Value = L.T($"صف {i + 1}", $"Row {i + 1}");
    }

    void Collect()
    {
        ResultHeaders = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText ?? "").ToArray();
        ResultRows.Clear();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var vals = new string[grid.Columns.Count];
            for (int c = 0; c < grid.Columns.Count; c++)
                vals[c] = row.Cells[c].Value?.ToString() ?? "";
            // الصفوف الفارغة كلياً لا تُكتب في الجدول
            if (vals.All(v => v.Trim().Length == 0)) continue;
            ResultRows.Add(vals);
        }
    }

    void Say(string message)
    {
        try
        {
            grid.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent,
                message);
        }
        catch { }
    }
}

/// <summary>إعدادات التطبيق: اللغة، تنسيق التاريخ، مجلد النسخ الاحتياطي.</summary>
public class SettingsForm : Form
{
    static readonly string[] DateFormatKeys = { "arabic", "algerian", "english", "numeric" };

    readonly Settings settings;
    readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox dateFormat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox backupPath = new();

    public bool LanguageChanged { get; private set; }

    public SettingsForm(Settings settings)
    {
        this.settings = settings;
        Text = L.T("إعدادات دفتري", "Daftari settings");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 360);

        int y = 16;
        void AddLabel(string text)
        {
            Controls.Add(new Label { Text = text, Left = 16, Top = y, Width = 528 });
            y += 26;
        }

        AddLabel(L.T("لغة التطبيق (تُطبق بعد إعادة التشغيل):", "Application language (applied after restart):"));
        language.SetBounds(16, y, 240, 32);
        language.AccessibleName = L.T("لغة التطبيق", "Application language");
        language.Items.AddRange(new object[] { "العربية", "English" });
        language.SelectedIndex = settings.Language == "en" ? 1 : 0;
        Controls.Add(language);
        y += 48;

        AddLabel(L.T("تنسيق إدراج التاريخ والوقت (Ctrl+Shift+T):", "Date and time format (Ctrl+Shift+T):"));
        var now = DateTime.Now;
        dateFormat.SetBounds(16, y, 460, 32);
        dateFormat.AccessibleName = L.T("تنسيق التاريخ والوقت", "Date and time format");
        dateFormat.Items.AddRange(new object[]
        {
            L.T("عربي — ", "Arabic — ") + Settings.FormatTimestamp("arabic", now),
            L.T("جزائري — ", "Algerian — ") + Settings.FormatTimestamp("algerian", now),
            L.T("إنجليزي — ", "English — ") + Settings.FormatTimestamp("english", now),
            L.T("رقمي — ", "Numeric — ") + Settings.FormatTimestamp("numeric", now),
        });
        int di = Array.IndexOf(DateFormatKeys, settings.DateFormat);
        dateFormat.SelectedIndex = di < 0 ? 0 : di;
        Controls.Add(dateFormat);
        y += 48;

        AddLabel(L.T("مجلد النسخ الاحتياطي:", "Backup folder:"));
        backupPath.SetBounds(16, y, 380, 32);
        backupPath.AccessibleName = L.T("مسار مجلد النسخ الاحتياطي", "Backup folder path");
        backupPath.Text = settings.BackupFolder ?? "";
        var browse = new Button { Text = L.T("استعراض...", "Browse..."), Width = 110 };
        browse.SetBounds(408, y - 1, 130, 30);
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = L.T("اختر مجلد النسخ الاحتياطي", "Choose the backup folder"),
                UseDescriptionForTitle = true,
                SelectedPath = backupPath.Text
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) backupPath.Text = dlg.SelectedPath;
        };
        Controls.Add(backupPath);
        Controls.Add(browse);
        y += 36;
        Controls.Add(new Label
        {
            Text = L.T("تلميح: اختر مجلداً داخل Google Drive أو OneDrive لرفع النسخ إلى السحابة تلقائياً.",
                       "Tip: choose a folder inside Google Drive or OneDrive to sync backups to the cloud automatically."),
            Left = 16, Top = y, Width = 528, Height = 40
        });
        y += 52;

        var ok = new Button { Text = L.T("حفظ", "Save"), DialogResult = DialogResult.OK, Width = 110 };
        ok.SetBounds(16, y, 110, 34);
        var cancel = new Button { Text = L.T("إلغاء", "Cancel"), DialogResult = DialogResult.Cancel, Width = 110 };
        cancel.SetBounds(134, y, 110, 34);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) => Apply();
    }

    void Apply()
    {
        var newLang = language.SelectedIndex == 1 ? "en" : "ar";
        LanguageChanged = newLang != settings.Language;
        settings.Language = newLang;
        settings.DateFormat = DateFormatKeys[Math.Max(0, dateFormat.SelectedIndex)];
        settings.BackupFolder = backupPath.Text.Trim().Length > 0 ? backupPath.Text.Trim() : null;
        settings.Save();
    }
}

/// <summary>
/// مدير المحذوفات: قائمة يقرؤها NVDA بالعناصر المحذوفة، مع استرجاع أو حذف نهائي أو إفراغ.
/// </summary>
public class TrashForm : Form
{
    readonly Vault vault;
    readonly ListBox list = new();
    readonly List<string> items = new();
    readonly Button restoreBtn = new();
    readonly Button deleteBtn = new();
    readonly Button emptyBtn = new();

    /// <summary>هل تغيّرت السلة (استرجاع/حذف) فتحتاج الشجرة تحديثاً؟</summary>
    public bool Changed { get; private set; }

    public TrashForm(Vault vault)
    {
        this.vault = vault;
        Text = L.T("المحذوفات", "Trash");
        RightToLeft = L.Rtl;
        RightToLeftLayout = L.RtlLayout;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 460);
        KeyPreview = true;

        list.Dock = DockStyle.Fill;
        list.AccessibleName = L.T("العناصر المحذوفة", "Deleted items");
        list.IntegralHeight = false;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
        restoreBtn.Text = L.T("استرجاع", "Restore");
        restoreBtn.AutoSize = true;
        restoreBtn.Click += (_, _) => Restore();
        deleteBtn.Text = L.T("حذف نهائي", "Delete permanently");
        deleteBtn.AutoSize = true;
        deleteBtn.Click += (_, _) => DeletePerm();
        emptyBtn.Text = L.T("إفراغ المحذوفات", "Empty trash");
        emptyBtn.AutoSize = true;
        emptyBtn.Click += (_, _) => Empty();
        var close = new Button { Text = L.T("إغلاق", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { restoreBtn, deleteBtn, emptyBtn, close });

        Controls.Add(list);
        Controls.Add(buttons);
        CancelButton = close;

        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Restore(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Delete) { DeletePerm(); e.SuppressKeyPress = true; }
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        Refill();
        Shown += (_, _) => list.Focus();
    }

    void Refill()
    {
        list.BeginUpdate();
        list.Items.Clear();
        items.Clear();
        foreach (var p in vault.TrashItems().OrderByDescending(GetTime))
        {
            items.Add(p);
            var kind = Directory.Exists(p) ? L.T("مجلد", "folder") : L.T("ملاحظة", "note");
            var name = Directory.Exists(p) ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p);
            var when = GetTime(p).ToString("yyyy-MM-dd HH:mm");
            list.Items.Add($"{name} — {kind} — {when}");
        }
        list.EndUpdate();
        bool any = items.Count > 0;
        restoreBtn.Enabled = deleteBtn.Enabled = emptyBtn.Enabled = any;
        if (any) list.SelectedIndex = 0;
        else list.Items.Add(L.T("لا توجد عناصر محذوفة", "No deleted items"));
    }

    static DateTime GetTime(string p)
    {
        try { return Directory.Exists(p) ? Directory.GetLastWriteTime(p) : File.GetLastWriteTime(p); }
        catch { return DateTime.MinValue; }
    }

    string? Selected => list.SelectedIndex >= 0 && list.SelectedIndex < items.Count ? items[list.SelectedIndex] : null;

    void Restore()
    {
        var p = Selected;
        if (p == null) return;
        try
        {
            var dest = vault.RestoreFromTrash(p);
            Changed = true;
            var name = Directory.Exists(dest) ? Path.GetFileName(dest) : Path.GetFileNameWithoutExtension(dest);
            Refill();
            Say(L.T($"استُرجع {name} إلى جذر القبو", $"Restored {name} to the vault root"));
        }
        catch (Exception ex) { Fail(ex); }
    }

    void DeletePerm()
    {
        var p = Selected;
        if (p == null) return;
        var name = Directory.Exists(p) ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p);
        var answer = MessageBox.Show(this,
            L.T($"حذف \"{name}\" نهائياً؟ لا يمكن التراجع.", $"Delete \"{name}\" permanently? This cannot be undone."),
            L.T("حذف نهائي", "Delete permanently"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2, L.MsgOptions);
        if (answer != DialogResult.Yes) return;
        try
        {
            vault.DeletePermanently(p);
            Changed = true;
            Refill();
            Say(L.T($"حُذف {name} نهائياً", $"{name} permanently deleted"));
        }
        catch (Exception ex) { Fail(ex); }
    }

    void Empty()
    {
        if (items.Count == 0) return;
        var answer = MessageBox.Show(this,
            L.T($"إفراغ المحذوفات نهائياً؟ سيُمحى {items.Count} عنصراً بلا رجعة.",
                $"Empty the trash permanently? {items.Count} items will be erased with no undo."),
            L.T("إفراغ المحذوفات", "Empty trash"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2, L.MsgOptions);
        if (answer != DialogResult.Yes) return;
        try
        {
            vault.EmptyTrash();
            Changed = true;
            Refill();
            Say(L.T("أُفرغت المحذوفات", "Trash emptied"));
        }
        catch (Exception ex) { Fail(ex); }
    }

    void Fail(Exception ex) =>
        MessageBox.Show(this, L.T("تعذّرت العملية: ", "Operation failed: ") + ex.Message,
            L.T("المحذوفات", "Trash"), MessageBoxButtons.OK, MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1, L.MsgOptions);

    void Say(string message)
    {
        try
        {
            list.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent, message);
        }
        catch { }
    }
}
