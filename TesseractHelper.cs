using System.Diagnostics;
using Tesseract;

namespace ClipTool;

public class TesseractHelper
{
    private readonly string _dataPath;
    private Task? _dataInitTask;
    private static readonly object _initLock = new();

    // Language pack: English + Simplified Chinese
    private const string Lang = "eng+chi_sim";

    public TesseractHelper()
    {
        _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    }

    /// <summary>
    /// Singleton init task — runs once (downloads/validates tessdata),
    /// subsequent callers await the same task.
    /// </summary>
    public Task DataReady
    {
        get
        {
            if (_dataInitTask != null) return _dataInitTask;
            lock (_initLock)
            {
                _dataInitTask ??= InitDataAsync();
            }
            return _dataInitTask;
        }
    }

    private async Task InitDataAsync()
    {
        var baseUrl = "https://github.com/tesseract-ocr/tessdata_best/raw/main";
        var langs = new[] { "eng", "chi_sim" };

        Directory.CreateDirectory(_dataPath);

        foreach (var lang in langs)
        {
            var dataFile = Path.Combine(_dataPath, $"{lang}.traineddata");
            if (File.Exists(dataFile))
            {
                // Replace old tessdata_fast files (smaller size) with tessdata_best
                var fi = new FileInfo(dataFile);
                if ((lang == "eng" && fi.Length < 20_000_000) ||
                    (lang == "chi_sim" && fi.Length < 30_000_000))
                {
                    fi.Delete();
                }
                else
                {
                    continue;
                }
            }

            var url = $"{baseUrl}/{lang}.traineddata";
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);

            Debug.WriteLine($"Downloading OCR language data ({lang}, ~60MB)...");
            var data = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(dataFile, data);
            Debug.WriteLine($"OCR language data ({lang}) downloaded");
        }
    }

    /// <summary>
    /// Run OCR on the given bitmap. Supports cancellation.
    /// </summary>
    public async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                using var engine = new TesseractEngine(_dataPath, Lang, EngineMode.LstmOnly);
                engine.SetVariable("tessedit_char_whitelist", "");
                engine.DefaultPageSegMode = PageSegMode.SingleBlock;

                // Chinese text optimization: preserve inter-word spacing, avoid over-splitting
                engine.SetVariable("preserve_interword_spaces", "1");

                ct.ThrowIfCancellationRequested();

                using var pix = ConvertBitmapToPix(bitmap);

                ct.ThrowIfCancellationRequested();

                using var page = engine.Process(pix);

                ct.ThrowIfCancellationRequested();

                var text = page.GetText()?.Trim() ?? "";

                // Post-process: collapse excessive blank lines while keeping paragraph breaks
                text = CollapseExcessiveNewlines(text);

                return text;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"OCR failed: {ex.Message}");
                return "";
            }
        }, ct);
    }

    private static Pix ConvertBitmapToPix(Bitmap bitmap)
    {
        // Round-trip via PNG memory stream for Leptonica to load
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Seek(0, SeekOrigin.Begin);
        return Pix.LoadFromMemory(ms.ToArray());
    }

    /// <summary>
    /// Collapse excessive blank lines: 2+ consecutive empty lines become 1,
    /// trim leading/trailing whitespace per line.
    /// </summary>
    private static string CollapseExcessiveNewlines(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var lines = text.Split('\n');
        var result = new List<string>();
        var blankCount = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                blankCount++;
                if (blankCount <= 1)
                    result.Add(""); // keep 1 blank line as paragraph separator
            }
            else
            {
                blankCount = 0;
                result.Add(trimmed);
            }
        }

        // Strip trailing empty lines
        while (result.Count > 0 && result[^1].Length == 0)
            result.RemoveAt(result.Count - 1);

        return string.Join(Environment.NewLine, result);
    }
}
