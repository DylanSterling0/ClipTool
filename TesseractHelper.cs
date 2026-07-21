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
    /// Singleton init task — runs once on success, retries on failure.
    /// If a previous download attempt failed, subsequent callers trigger a retry.
    /// </summary>
    public Task DataReady
    {
        get
        {
            if (_dataInitTask is { IsCompletedSuccessfully: true }) return _dataInitTask;
            lock (_initLock)
            {
                if (_dataInitTask is { IsCompletedSuccessfully: true }) return _dataInitTask;
                // Allow retry if previous attempt faulted
                _dataInitTask = InitDataAsync();
            }
            return _dataInitTask;
        }
    }

    private async Task InitDataAsync()
    {
        // Try tessdata_fast first (much smaller: ~2-12MB vs ~60MB for tessdata_best)
        var mirrors = new[]
        {
            // 1. Direct GitHub (tessdata_fast variant — ~2-12MB per file)
            "https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata",
            // 2. jsDelivr CDN mirror (works well in China)
            "https://cdn.jsdelivr.net/gh/tesseract-ocr/tessdata_fast@main/{lang}.traineddata",
        };
        var langs = new[] { "eng", "chi_sim" };

        Directory.CreateDirectory(_dataPath);

        foreach (var lang in langs)
        {
            var dataFile = Path.Combine(_dataPath, $"{lang}.traineddata");

            // If file exists and is reasonably large, keep it — skip download
            if (File.Exists(dataFile))
            {
                var fi = new FileInfo(dataFile);
                if (fi.Length >= 1_000_000) // at least 1MB = valid tessdata_fast
                    continue;

                // Truncated file from a prior failed download → remove, will retry
                fi.Delete();
            }

            var downloaded = false;
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(3);

            foreach (var mirror in mirrors)
            {
                var url = mirror.Replace("{lang}", lang);
                try
                {
                    Debug.WriteLine($"Downloading {lang} from {mirror.Split('/')[2]}...");
                    var data = await http.GetByteArrayAsync(url);

                    // Write to temp file first, then rename to avoid partial writes
                    var tmpFile = dataFile + ".tmp";
                    await File.WriteAllBytesAsync(tmpFile, data);
                    if (File.Exists(dataFile)) File.Delete(dataFile);
                    File.Move(tmpFile, dataFile);

                    Debug.WriteLine($"OCR language data ({lang}) downloaded from {mirror.Split('/')[2]} ({data.Length / 1024}KB)");
                    downloaded = true;
                    break;
                }
                catch (HttpRequestException ex)
                {
                    Debug.WriteLine($"Mirror {mirror.Split('/')[2]} failed for {lang}: {ex.Message}");
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($"Mirror {mirror.Split('/')[2]} timed out for {lang}");
                }
            }

            if (!downloaded)
                Debug.WriteLine($"All mirrors failed for {lang} — OCR for this language unavailable");
        }
    }

    /// <summary>
    /// Check whether required OCR language data exists on disk.
    /// </summary>
    public bool HasRequiredData()
    {
        var langs = Lang.Split('+');
        return langs.All(lang =>
        {
            var file = Path.Combine(_dataPath, $"{lang}.traineddata");
            return File.Exists(file) && new FileInfo(file).Length >= 1_000_000;
        });
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
