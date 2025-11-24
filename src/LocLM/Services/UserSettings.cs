namespace LocLM.Services;

public class UserSettings
{
    public bool ShowSidebar { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public double EditorFontSize { get; set; } = 12;
    public int CommandTimeoutMs { get; set; } = 60000;
    public int PerCommandTimeoutMs { get; set; } = 60000;
}
