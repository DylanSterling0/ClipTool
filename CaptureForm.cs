using System.Drawing.Imaging;

namespace ClipTool;

public partial class CaptureForm : Form
{
    private Point _startPoint;
    private Rectangle _selection;
    private bool _selecting;
    private Bitmap _fullScreen;
    private readonly TesseractHelper _ocr;
    private bool _hasResult;
    private CancellationTokenSource? _cts;
    private bool _cancelled;

    public string? OcrResult { get; private set; }

    public CaptureForm(TesseractHelper ocr)
    {
        _ocr = ocr;
        Text = "Screenshot OCR — Drag to select region";
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        ShowInTaskbar = false;

        // Full-screen capture
        var bounds = Screen.PrimaryScreen!.Bounds;
        _fullScreen = new Bitmap(bounds.Width, bounds.Height);
        using var g = Graphics.FromImage(_fullScreen);
        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

        Paint += OnPaint!;
        MouseDown += OnMouseDown!;
        MouseMove += OnMouseMove!;
        MouseUp += OnMouseUp!;
        KeyDown += OnKeyDown!;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        // Right-click → cancel current operation (selection or recognition)
        if (e.Button == MouseButtons.Right)
        {
            CancelOperation();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        _startPoint = e.Location;
        _selecting = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_selecting)
        {
            _selection = new Rectangle(
                Math.Min(_startPoint.X, e.X),
                Math.Min(_startPoint.Y, e.Y),
                Math.Abs(_startPoint.X - e.X),
                Math.Abs(_startPoint.Y - e.Y));
            Invalidate();
        }
    }

    private async void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) return; // handled by OnMouseDown

        if (!_selecting) return;
        _selecting = false;

        // Ignore tiny selections (accidental clicks)
        if (_selection.Width < 10 || _selection.Height < 10)
        {
            _selection = Rectangle.Empty;
            Invalidate();
            return;
        }

        // Crop selected region
        using var cropped = new Bitmap(_selection.Width, _selection.Height);
        using var g = Graphics.FromImage(cropped);
        g.DrawImage(_fullScreen, 0, 0, _selection, GraphicsUnit.Pixel);

        // Async OCR — does not block the UI thread
        _cts = new CancellationTokenSource();
        _hasResult = true;
        _cancelled = false;
        Invalidate(); // show "Recognizing..."

        var result = await _ocr.RecognizeAsync(cropped, _cts.Token);

        // User cancelled during OCR (right-click/ESC)
        if (_cancelled || IsDisposed) return;

        OcrResult = result;

        DialogResult = string.IsNullOrEmpty(result) ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Cancel current operation: selection or recognition
    /// </summary>
    private void CancelOperation()
    {
        if (_hasResult && !_cancelled)
        {
            // Recognition in progress → signal cancellation
            _cancelled = true;
            _cts?.Cancel();
        }

        // Reset selection state
        _selecting = false;
        _selection = Rectangle.Empty;
        _hasResult = false;

        if (!IsDisposed)
        {
            DialogResult = DialogResult.Cancel;
            Invalidate();
            Close();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            CancelOperation();
        }
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        if (_fullScreen == null) return;

        if (_hasResult)
        {
            // Recognition overlay
            e.Graphics.DrawImage(_fullScreen, 0, 0);
            using var brush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            e.Graphics.FillRectangle(brush, 0, 0, _fullScreen.Width, _fullScreen.Height);
            TextRenderer.DrawText(e.Graphics, "Recognizing text...",
                new Font("Microsoft YaHei", 16),
                new Rectangle(0, 0, _fullScreen.Width, _fullScreen.Height),
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Draw screenshot + mask
        e.Graphics.DrawImage(_fullScreen, 0, 0);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            // Semi-transparent mask outside selection
            using var brush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
            // Top
            e.Graphics.FillRectangle(brush, 0, 0, _fullScreen.Width, _selection.Top);
            // Bottom
            e.Graphics.FillRectangle(brush, 0, _selection.Bottom, _fullScreen.Width, _fullScreen.Height - _selection.Bottom);
            // Left
            e.Graphics.FillRectangle(brush, 0, _selection.Top, _selection.Left, _selection.Height);
            // Right
            e.Graphics.FillRectangle(brush, _selection.Right, _selection.Top, _fullScreen.Width - _selection.Right, _selection.Height);

            // Selection border
            using var pen = new Pen(Color.FromArgb(0, 120, 215), 2);
            e.Graphics.DrawRectangle(pen, _selection);

            // Size indicator
            var sizeText = $"{_selection.Width} x {_selection.Height}";
            TextRenderer.DrawText(e.Graphics, sizeText,
                new Font("Consolas", 9),
                new Point(_selection.Right + 4, _selection.Top),
                Color.White, Color.FromArgb(0, 120, 215));
        }
        else
        {
            // No selection — full-screen light overlay
            using var brush = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            e.Graphics.FillRectangle(brush, 0, 0, _fullScreen.Width, _fullScreen.Height);
        }

        // Top hint bar
        using var hintBg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        e.Graphics.FillRectangle(hintBg, 0, 0, _fullScreen.Width, 36);
        TextRenderer.DrawText(e.Graphics,
            "Drag to select  |  Right-click / ESC to cancel  |  Release to recognize",
            new Font("Microsoft YaHei", 10),
            new Rectangle(0, 0, _fullScreen.Width, 36),
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
