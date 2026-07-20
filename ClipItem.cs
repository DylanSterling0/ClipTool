namespace ClipTool;

public enum ClipType
{
    Text,
    Ocr
}

public class ClipItem
{
    public string Text { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public ClipType Type { get; set; } = ClipType.Text;

    public string DisplayText => Text.Length > 80 ? Text[..80] + "..." : Text;
    public string TimeStr => Time.ToString("MM-dd HH:mm");
}
