#pragma warning disable CS8602
#nullable enable

namespace ClipTool;

public partial class HistoryForm : Form
{
    private readonly List<ClipItem> _items;
    private readonly TextBox _searchBox;
    private readonly ListBox _listBox;

    public event Action<string>? OnItemSelected;
    public event Action? OnItemDeleted;

    public HistoryForm(List<ClipItem> items)
    {
        _items = items;
        Text = "Clipboard History";
        Size = new Size(580, 500);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei", 9);
        BackColor = Color.FromArgb(245, 245, 245);

        // Search bar
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 6, 8, 4),
            ColumnCount = 2,
            RowCount = 1,
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei", 10),
            ForeColor = Color.FromArgb(60, 60, 60),
        };
        _searchBox.TextChanged += (_, _) => RefreshList();
        _searchBox.KeyDown += (_, e) =>
        {
            if (e == null) return;
            if (e.KeyCode == Keys.Enter && _listBox.Items.Count > 0)
            {
                _listBox.SelectedIndex = 0;
                SelectCurrentItem();
            }
            if (e.KeyCode == Keys.Down)
                _listBox.SelectedIndex = Math.Min(_listBox.SelectedIndex + 1, _listBox.Items.Count - 1);
            if (e.KeyCode == Keys.Up)
                _listBox.SelectedIndex = Math.Max(_listBox.SelectedIndex - 1, 0);
            if (e.KeyCode == Keys.Delete && _listBox.SelectedItem is ClipItem delItem)
                DeleteItem(delItem);
        };

        var hintLabel = new Label
        {
            Text = "🔍",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12),
            Margin = new Padding(6, 4, 4, 0),
        };

        topPanel.Controls.Add(_searchBox, 0, 0);
        topPanel.Controls.Add(hintLabel, 1, 0);

        // Context menu
        var contextMenu = new ContextMenuStrip();
        var deleteItem = new ToolStripMenuItem("🗑️ Delete", null, (_, _) =>
        {
            if (_listBox.SelectedItem is ClipItem item)
                DeleteItem(item);
        });
        contextMenu.Items.Add(deleteItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("🧹 Clear All", null, (_, _) =>
        {
            if (MessageBox.Show("Clear all history?", "Clear", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                == DialogResult.Yes)
            {
                _items.Clear();
                RefreshList();
                OnItemDeleted?.Invoke();
            }
        }));

        // List
        _listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei", 10),
            DrawMode = DrawMode.OwnerDrawVariable,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            IntegralHeight = false,
            ContextMenuStrip = contextMenu,
        };
        _listBox.DrawItem += OnDrawItem!;
        _listBox.MeasureItem += OnMeasureItem!;
        _listBox.MouseDoubleClick += (_, _) => SelectCurrentItem();
        _listBox.KeyDown += (_, e) =>
        {
            if (e == null) return;
            if (e.KeyCode == Keys.Enter) SelectCurrentItem();
            if (e.KeyCode == Keys.Escape) Close();
            if (e.KeyCode == Keys.Delete && _listBox.SelectedItem is ClipItem delItem)
                DeleteItem(delItem);
        };
        _listBox.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = _listBox.IndexFromPoint(e.Location);
                if (index >= 0)
                    _listBox.SelectedIndex = index;
            }
        };

        // Status bar
        var statusPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            BackColor = Color.FromArgb(235, 235, 235),
        };

        var statusLabel = new Label
        {
            Text = "Double-click to paste  |  Del to delete  |  ESC to close  |  Right-click for more",
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.Gray,
            Padding = new Padding(8, 0, 0, 0),
        };
        statusPanel.Controls.Add(statusLabel);

        Controls.Add(_listBox);
        Controls.Add(topPanel);
        Controls.Add(statusPanel);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e == null) return;
            if (e.Control && e.KeyCode == Keys.F) _searchBox.Focus();
            if (e.KeyCode == Keys.Escape) Close();
        };

        Load += (_, _) => _searchBox.Focus();
        RefreshList();
    }

    private void RefreshList()
    {
        var query = _searchBox.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(query)
            ? _items
            : _items.Where(i => i.Text.ToLower().Contains(query)).ToList();

        _listBox.BeginUpdate();
        _listBox.DataSource = null;
        if (filtered.Count > 0)
        {
            _listBox.DataSource = filtered;
            _listBox.DisplayMember = nameof(ClipItem.Text);
        }
        _listBox.EndUpdate();
    }

    private void DeleteItem(ClipItem item)
    {
        _items.Remove(item);
        RefreshList();
        OnItemDeleted?.Invoke();
    }

    private void OnMeasureItem(object? sender, MeasureItemEventArgs e)
    {
        e.ItemHeight = 56;
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        if (_listBox.DataSource is not List<ClipItem> items || e.Index >= items.Count) return;

        var item = items[e.Index];

        // Background
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var backColor = isSelected
            ? Color.FromArgb(220, 235, 252)
            : e.Index % 2 == 0 ? Color.White : Color.FromArgb(250, 250, 252);
        e.Graphics.FillRectangle(new SolidBrush(backColor), e.Bounds);

        // Time
        using var timeFont = new Font("Consolas", 8);
        TextRenderer.DrawText(e.Graphics, item.TimeStr, timeFont,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 4, 130, 16),
            Color.Gray, TextFormatFlags.Default);

        // OCR badge
        if (item.Type == ClipType.Ocr)
        {
            var badgeRect = new Rectangle(e.Bounds.X + 140, e.Bounds.Y + 4, 36, 14);
            using var badgeBrush = new SolidBrush(Color.FromArgb(70, 130, 180));
            e.Graphics.FillRectangle(badgeBrush, badgeRect);
            TextRenderer.DrawText(e.Graphics, "OCR", new Font("Microsoft YaHei", 7),
                badgeRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // Text content
        var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 22, e.Bounds.Width - 20, 28);
        TextRenderer.DrawText(e.Graphics, item.DisplayText,
            new Font("Microsoft YaHei", 10), textRect,
            isSelected ? Color.FromArgb(0, 0, 0) : Color.FromArgb(50, 50, 50),
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }

    private void SelectCurrentItem()
    {
        if (_listBox.SelectedItem is ClipItem item)
        {
            OnItemSelected?.Invoke(item.Text);
            Close();
        }
    }
}
