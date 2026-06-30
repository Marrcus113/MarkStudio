using System;
using System.IO;
using System.Text.Json;

namespace MarkStudio.Models;

public class AppSettings
{
    public string CloudflareAccountId { get; set; } = "";
    public string CloudflareApiToken { get; set; } = "";
    public string AiModel { get; set; } = "@cf/qwen/qwen2.5-coder-32b-instruct";
    public string ChatModel { get; set; } = "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
    public string ThemeName { get; set; } = "Monokai";
    public bool IsDarkTheme { get; set; } = true;
    public string LastOpenedFolder { get; set; } = "";

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarkStudio");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
