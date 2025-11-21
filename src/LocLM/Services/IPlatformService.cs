using System;
using System.IO;

namespace LocLM.Services;

public interface IPlatformService
{
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
    string GetConfigDirectory();
    string GetPythonCommand();
}

public class PlatformService : IPlatformService
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string GetConfigDirectory()
    {
        if (IsWindows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocLM");
        if (IsMacOS)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "LocLM");

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "loclm");
    }

    public string GetPythonCommand()
    {
        return IsWindows ? "python" : "python3";
    }
}
