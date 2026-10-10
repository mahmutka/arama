using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Arama.App;
using Arama.Index;
using Arama.Search;

namespace Arama.UI
{
    sealed class MainForm : Form
    {
        static readonly string[] ColumnTitles = { "Ad", "Yol", "Boyut", "Değiştirilme tarihi" };

        readonly TextBox searchBox = new TextBox();
        readonly ComboBox filterBox = new ComboBox();
        readonly ListView list = new BufferedListView();
        readonly StatusStrip statusStrip = new StatusStrip();
        readonly ToolStripStatusLabel resultLabel = new ToolStripStatusLabel();
        readonly ToolStripStatusLabel indexLabel = new ToolStripStatusLabel();
        readonly System.Windows.Forms.Timer debounceTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer trimTimer = new System.Windows.Forms.Timer();
        readonly IconCache icons = new IconCache();
        readonly List<VolumeIndexer> indexers = new List<VolumeIndexer>();
        readonly List<string> skippedDrives = new List<string>();
        readonly Settings settings;
        readonly TrayIcon tray;
        SearchResult current;
        // current'ı üreten arama: artımlı güncellemeler aynı koşullarla yapılır
        VolumeIndexer[] currentSources;
        Query currentQuery;
        string currentQueryText;
        FilterKind currentFilter;
        SortColumn currentSort;
        bool currentDescending;
        CancellationTokenSource searchCts;
        volatile bool indexDirty;
        bool searchRunning;
        SortColumn sortColumn = SortColumn.Name;
        bool sortDescending;
        bool allowVisible;
        bool shownOnce;
        bool exiting;
        bool hotkeyWarningPending;

        public MainForm(bool startHidden)
        {
            allowVisible = !startHidden;
            settings = Settings.Load(AppPaths.SettingsFile);
            Text = "Arama";
            Icon = AppIcon.Create();
            ApplyWindowSettings();
            BuildLayout();
            tray = new TrayIcon(this, Icon);
            if (hotkeyWarningPending) ShowHotkeyWarning();
            debounceTimer.Interval = 30;
            debounceTimer.Tick += (s, e) => { debounceTimer.Stop(); StartSearch(false); };
            refreshTimer.Interval = 250;
            refreshTimer.Tick += (s, e) => OnRefreshTick();
            refreshTimer.Start();
            // Aramaların geçici dizileri birikince belleği geri ver (yalnızca eşik aşılınca)
            trimTimer.Interval = 5000;
            trimTimer.Tick += (s, e) => { if (!searchRunning) ThreadPool.QueueUserWorkItem(_ => MemoryTrim.IfGrown(64L << 20)); };
            trimTimer.Start();
            StartIndexers();
        }

        // ---- Kurulum ----

        void BuildLayout()
        {
            searchBox.Dock = DockStyle.Fill;
            searchBox.TextChanged += (s, e) => { debounceTimer.Stop(); debounceTimer.Start(); };
            searchBox.KeyDown += OnSearchKeyDown;

            filterBox.Dock = DockStyle.Right;
            filterBox.DropDownStyle = ComboBoxStyle.DropDownList;
            filterBox.Width = LogicalToDeviceUnits(130);
            filterBox.Items.AddRange(Filters.DisplayNames);
            filterBox.SelectedIndex = 0;   // her açılışta Tümü
            filterBox.SelectedIndexChanged += (s, e) => StartSearch(false);

            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Padding = new Padding(4);
            top.Height = Math.Max(searchBox.PreferredHeight, filterBox.PreferredHeight) + 8;
            top.Controls.Add(searchBox);
            top.Controls.Add(filterBox);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.VirtualMode = true;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.SmallImageList = icons.Images;
            for (int i = 0; i < ColumnTitles.Length; i++)
                list.Columns.Add(ColumnTitles[i], settings.ColumnWidths[i], i == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            list.RetrieveVirtualItem += OnRetrieveItem;
            list.ColumnClick += OnColumnClick;
            list.DoubleClick += (s, e) => OpenSelected();
            list.KeyDown += OnListKeyDown;
            list.ItemDrag += OnItemDrag;
            list.ContextMenuStrip = BuildItemMenu();
            UpdateSortGlyph();

            resultLabel.Spring = true;
            resultLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusStrip.ShowItemToolTips = true;
            statusStrip.Items.Add(resultLabel);
            statusStrip.Items.Add(indexLabel);

            Controls.Add(list);
            Controls.Add(top);
            Controls.Add(statusStrip);
        }

        ContextMenuStrip BuildItemMenu()
        {
            var open = new ToolStripMenuItem("Aç", null, (s, e) => OpenSelected());
            open.ShortcutKeyDisplayString = "Enter";
            var folder = new ToolStripMenuItem("Klasörünü aç", null, (s, e) => ShowSelectedInFolder());
            folder.ShortcutKeyDisplayString = "Ctrl+Enter";
            var copy = new ToolStripMenuItem("Yolu kopyala", null, (s, e) => CopySelectedPaths());
            copy.ShortcutKeyDisplayString = "Ctrl+C";
            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { open, folder, copy });
            return menu;
        }

        void ApplyWindowSettings()
        {
            Size = new Size(settings.Width, settings.Height);
            var bounds = new Rectangle(settings.X, settings.Y, settings.Width, settings.Height);
            if (settings.X != -1 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(settings.X, settings.Y);
            }
            else StartPosition = FormStartPosition.CenterScreen;
        }

        void SaveSettings()
        {
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.X = b.X;
            settings.Y = b.Y;
            settings.Width = b.Width;
            settings.Height = b.Height;
            for (int i = 0; i < list.Columns.Count; i++) settings.ColumnWidths[i] = list.Columns[i].Width;
            settings.Save(AppPaths.SettingsFile);
        }

        void StartIndexers()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                string format;
                try
                {
                    if (!drive.IsReady) continue;
                    format = drive.DriveFormat;
                }
                catch (IOException) { continue; }
                char letter = drive.Name[0];
                if (format != "NTFS")
                {
                    skippedDrives.Add(letter + ": (" + format + ")");
                    continue;
                }
                var indexer = new VolumeIndexer(letter);
                indexer.IndexChanged += () => { indexDirty = true; };
                indexers.Add(indexer);
                indexer.Start();
            }
        }

        // ---- Arama ----

        async void StartSearch(bool keepSelection)
        {
            string text = searchBox.Text;
            Query query = Query.Parse(text);
            var filter = (FilterKind)Math.Max(0, filterBox.SelectedIndex);
            SortColumn sort = sortColumn;
            bool descending = sortDescending;
            // Kullanıcı mevcut sorguya harf/terim eklediyse yalnızca mevcut sonuçlar taranır
            bool narrow = !keepSelection && current != null && currentSources != null
                && filter == currentFilter && sort == currentSort && descending == currentDescending
                && !SourcesChanged() && Query.CanNarrow(currentQueryText, text);
            if (!narrow)
            {
                // Bekleyen değişiklikler bu tam aramaya dahil olur; sonrakiler artımlı uygulanır.
                // (Daraltmada beklemede kalırlar; yenileme zamanlayıcısı yeni sorguyla uygular.)
                foreach (var ix in indexers)
                {
                    int[] ignored;
                    ix.Changes.Take(out ignored);
                }
            }
            VolumeIndexer[] sources = narrow ? currentSources : indexers.Where(ix => ix.Index != null).ToArray();
            FileIndex[] indexes = sources.Select(ix => ix.Index).ToArray();
            SearchResult baseResult = current;
            if (searchCts != null) searchCts.Cancel();
            var cts = new CancellationTokenSource();
            searchCts = cts;
            List<ResultRef> previous = keepSelection ? SelectedRefs(1000) : null;
            searchRunning = true;
            SearchResult result;
            try
            {
                result = await Task.Run(() => narrow
                    ? SearchEngine.Narrow(baseResult, query, filter, cts.Token)
                    : SearchEngine.Run(indexes, query, filter, sort, descending, cts.Token));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                if (cts == searchCts)
                {
                    searchRunning = false;
                    resultLabel.Text = "Arama hatası: " + ex.Message;
                }
                return;
            }
            if (cts != searchCts) return;
            searchRunning = false;
            currentSources = sources;
            currentQuery = query;
            currentQueryText = text;
            currentFilter = filter;
            currentSort = sort;
            currentDescending = descending;
            ShowResult(result, previous);
        }

        /// <summary>Diskteki değişiklikleri mevcut sonuca artımlı uygular (tam taramadan çok ucuz).</summary>
        async void StartIncremental(List<KeyValuePair<int, int[]>> work)
        {
            var cts = new CancellationTokenSource();
            searchCts = cts;
            SearchResult baseResult = current;
            Query query = currentQuery;
            FilterKind filter = currentFilter;
            SortColumn sort = currentSort;
            bool descending = currentDescending;
            List<ResultRef> previous = SelectedRefs(1000);
            searchRunning = true;
            SearchResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    SearchResult r = baseResult;
                    foreach (var item in work)
                    {
                        r = SearchEngine.Update(r, item.Key, item.Value, query, filter, sort, descending);
                        if (r == null) break;
                    }
                    return r;
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                result = null;
            }
            if (cts != searchCts) return;   // bu arada kullanıcı yeni arama başlattı
            searchRunning = false;
            if (result == null) StartSearch(true);
            else if (!ReferenceEquals(result, current)) ShowResult(result, previous);
        }

        void ShowResult(SearchResult result, List<ResultRef> previous)
        {
            current = result;
            list.BeginUpdate();
            try
            {
                list.SelectedIndices.Clear();
                list.VirtualListSize = result.Items.Length;
                if (previous != null && previous.Count > 0)
                {
                    List<int> positions = result.IndexesOf(previous);
                    foreach (int i in positions) list.SelectedIndices.Add(i);
                    if (positions.Count > 0) list.FocusedItem = list.Items[positions[0]];
                }
                else if (previous == null && result.Items.Length > 0)
                {
                    list.EnsureVisible(0);
                }
            }
            finally { list.EndUpdate(); }
            list.Invalidate();
            resultLabel.Text = result.TotalLive.ToString("N0", Format.Turkish) + " nesne · " +
                result.Items.Length.ToString("N0", Format.Turkish) + " sonuç · " + result.ElapsedMs + " ms" +
                (result.Error != null ? " · " + result.Error : "");
        }

        void OnRefreshTick()
        {
            UpdateIndexStatus();
            if (!indexDirty || searchRunning || !Visible) return;
            indexDirty = false;
            if (current == null || currentSources == null || SourcesChanged())
            {
                StartSearch(true);
                return;
            }
            var work = new List<KeyValuePair<int, int[]>>();
            for (int v = 0; v < currentSources.Length; v++)
            {
                int[] ids;
                if (!currentSources[v].Changes.Take(out ids))
                {
                    StartSearch(true);
                    return;
                }
                if (ids.Length > 0) work.Add(new KeyValuePair<int, int[]>(v, ids));
            }
            if (work.Count > 0) StartIncremental(work);
        }

        /// <summary>Bir disk hazır oldu ya da indeksi yeniden kuruldu mu? (tam arama gerekir)</summary>
        bool SourcesChanged()
        {
            VolumeIndexer[] now = indexers.Where(ix => ix.Index != null).ToArray();
            if (now.Length != currentSources.Length) return true;
            for (int v = 0; v < now.Length; v++)
                if (now[v] != currentSources[v] || now[v].Index != current.Indexes[v]) return true;
            return false;
        }

        void UpdateIndexStatus()
        {
            var parts = new List<string>();
            foreach (var ix in indexers) parts.Add(ix.Drive + ": " + ix.Status);
            foreach (string s in skippedDrives) parts.Add(s + " atlandı");
            if (parts.Count == 0) parts.Add("NTFS disk bulunamadı");
            string text = string.Join(" · ", parts);
            if (indexLabel.Text != text) indexLabel.Text = text;
            string tip = string.Join("\n", indexers.Where(ix => ix.CorruptRecords > 0)
                .Select(ix => ix.Drive + ": " + ix.CorruptRecords + " bozuk MFT kaydı atlandı"));
            if (indexLabel.ToolTipText != tip) indexLabel.ToolTipText = tip;
        }

        // ---- Liste ----

        void OnRetrieveItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (current == null || e.ItemIndex >= current.Items.Length)
            {
                e.Item = new ListViewItem(new[] { "", "", "", "" });
                return;
            }
            ResultRef r = current.Items[e.ItemIndex];
            FileIndex idx = current.Indexes[r.Volume];
            string name, folder;
            bool isDirectory, live;
            long size, modified;
            idx.Lock.EnterReadLock();
            try
            {
                name = idx.GetName(r.Id);
                folder = idx.GetDirectoryPath(r.Id);
                isDirectory = idx.IsDirectory(r.Id);
                live = idx.IsLive(r.Id);
                size = idx.GetSize(r.Id);
                modified = idx.GetModified(r.Id);
            }
            finally { idx.Lock.ExitReadLock(); }
            var item = new ListViewItem(new[] { name, folder, isDirectory ? "" : Format.Size(size), Format.Date(modified) }, icons.Get(name, isDirectory));
            if (!live) item.ForeColor = SystemColors.GrayText;
            e.Item = item;
        }

        void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            var column = (SortColumn)e.Column;
            if (column == sortColumn) sortDescending = !sortDescending;
            else
            {
                sortColumn = column;
                sortDescending = false;
            }
            UpdateSortGlyph();
            StartSearch(true);
        }

        void UpdateSortGlyph()
        {
            for (int i = 0; i < list.Columns.Count; i++)
                list.Columns[i].Text = ColumnTitles[i] + (i == (int)sortColumn ? (sortDescending ? " ▼" : " ▲") : "");
        }

        List<ResultRef> SelectedRefs(int max)
        {
            var refs = new List<ResultRef>();
            if (current == null) return refs;
            foreach (int i in list.SelectedIndices)
            {
                if (i < current.Items.Length) refs.Add(current.Items[i]);
                if (refs.Count >= max) break;
            }
            return refs;
        }

        string FullPath(ResultRef r)
        {
            FileIndex idx = current.Indexes[r.Volume];
            idx.Lock.EnterReadLock();
            try { return idx.GetFullPath(r.Id); }
            finally { idx.Lock.ExitReadLock(); }
        }

        /// <summary>Seçili yollar; seçim yoksa ilk sonuç (arama kutusunda Enter için).</summary>
        List<string> TargetPaths(int max)
        {
            List<string> paths = SelectedRefs(max).Select(FullPath).ToList();
            if (paths.Count == 0 && current != null && current.Items.Length > 0) paths.Add(FullPath(current.Items[0]));
            return paths;
        }

        static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        void ShowNotFound(string path)
        {
            MessageBox.Show(this, "Dosya bulunamadı:\n" + path, "Arama", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void OpenSelected()
        {
            foreach (string path in TargetPaths(20))
            {
                if (Exists(path)) ShellLauncher.Open(path);
                else ShowNotFound(path);
            }
        }

        void ShowSelectedInFolder()
        {
            List<string> paths = TargetPaths(1);
            if (paths.Count == 0) return;
            if (Exists(paths[0])) ShellLauncher.ShowInFolder(paths[0]);
            else ShowNotFound(paths[0]);
        }

        void CopySelectedPaths()
        {
            List<string> paths = SelectedRefs(100000).Select(FullPath).ToList();
            if (paths.Count > 0) Clipboard.SetText(string.Join("\r\n", paths));
        }

        void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            string[] paths = SelectedRefs(10000).Select(FullPath).Where(Exists).ToArray();
            if (paths.Length > 0)
                list.DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && list.VirtualListSize > 0)
            {
                list.Focus();
                list.SelectedIndices.Clear();
                list.SelectedIndices.Add(0);
                list.FocusedItem = list.Items[0];
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (e.Control) ShowSelectedInFolder();
                else OpenSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control) ShowSelectedInFolder();
            else if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.C && e.Control) CopySelectedPaths();
            else return;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        // ---- Görünürlük, tepsi, kısayol ----

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            shownOnce = true;
            searchBox.Focus();
            StartSearch(false);
        }

        public void ShowAndFocus()
        {
            allowVisible = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            searchBox.Focus();
            searchBox.SelectAll();
            // Gizliyken biriken değişiklikleri yenileme zamanlayıcısı artımlı uygular
        }

        public void HideToTray()
        {
            if (shownOnce) SaveSettings();
            Hide();
            ThreadPool.QueueUserWorkItem(_ => MemoryTrim.Now());
        }

        public void ExitApplication()
        {
            exiting = true;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                HideToTray();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            if (shownOnce) SaveSettings();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refreshTimer.Stop();
            trimTimer.Stop();
            tray.Dispose();
            if (IsHandleCreated) Hotkey.Unregister(Handle);
            foreach (var indexer in indexers) indexer.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Hotkey.Register(Handle))
            {
                if (tray != null) ShowHotkeyWarning();
                else hotkeyWarningPending = true;
            }
        }

        void ShowHotkeyWarning()
        {
            tray.ShowWarning("Ctrl+Alt+Space kısayolu başka bir program tarafından kullanılıyor.");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Hotkey.Id)
            {
                ShowAndFocus();
                return;
            }
            if (Program.ShowMessage != 0 && m.Msg == Program.ShowMessage)
            {
                ShowAndFocus();
                return;
            }
            base.WndProc(ref m);
        }
    }

    sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }
}
