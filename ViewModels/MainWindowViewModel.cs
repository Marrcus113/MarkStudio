using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MarkStudio.Models;
using MarkStudio.Services;

namespace MarkStudio.ViewModels;

public class EditorTabViewModel : INotifyPropertyChanged
{
    private string _title = "Без имени";
    private string _filePath = "";
    private string _content = "";
    private string _language = "plain";
    private bool _isModified;

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string FilePath { get => _filePath; set { _filePath = value; if (!string.IsNullOrEmpty(value)) { Title = Path.GetFileName(value); Language = DetectLanguage(value); } OnPropertyChanged(); } }
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }
    public string Language { get => _language; set { _language = value; OnPropertyChanged(); } }
    public bool IsModified { get => _isModified; set { _isModified = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static string DetectLanguage(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".cs" => "csharp", ".js" or ".jsx" or ".mjs" => "javascript", ".ts" or ".tsx" => "typescript",
        ".py" => "python", ".json" => "json", ".html" or ".htm" => "html", ".css" => "css",
        ".xml" or ".csproj" or ".axaml" or ".xaml" => "xml", ".md" => "markdown",
        ".yml" or ".yaml" => "yaml", ".sql" => "sql", ".sh" => "shell",
        ".cpp" or ".c" or ".h" => "c", ".rs" => "rust", ".go" => "go",
        ".rb" => "ruby", ".php" => "php", ".java" => "java", _ => "plain"
    };
}

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly FileService _fileService = new();
    private readonly CloudflareAiService _aiService = new();
    private readonly AppSettings _settings;

    private EditorTabViewModel? _activeTab;
    private string? _selectedFolder;
    private string _statusBarText = "MarkStudio";
    private string _chatInput = "";
    private bool _isAiPanelVisible;
    private bool _isSettingsVisible;
    private string _inputAccountId = "";
    private string _inputApiToken = "";
    private string _inputAiModel = "@cf/qwen/qwen2.5-coder-32b-instruct";
    private string _inputChatModel = "@cf/meta/llama-3.1-8b-instruct";
    private bool _isLoading;
    private string _chatResponse = "";

    public ObservableCollection<EditorTabViewModel> Tabs { get; } = new();
    public ObservableCollection<ChatMessage> ChatHistory { get; } = new();
    public ObservableCollection<FileItem> FileTree { get; } = new();

    public EditorTabViewModel? ActiveTab { get => _activeTab; set { _activeTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasActiveTab)); } }
    public bool HasActiveTab => ActiveTab != null;
    public string? SelectedFolder { get => _selectedFolder; set { _selectedFolder = value; OnPropertyChanged(); LoadFileTree(); } }
    public string StatusBarText { get => _statusBarText; set { _statusBarText = value; OnPropertyChanged(); } }
    public string ChatInput { get => _chatInput; set { _chatInput = value; OnPropertyChanged(); } }
    public bool IsAiPanelVisible { get => _isAiPanelVisible; set { _isAiPanelVisible = value; OnPropertyChanged(); } }
    public bool IsSettingsVisible { get => _isSettingsVisible; set { _isSettingsVisible = value; OnPropertyChanged(); } }
    public string InputAccountId { get => _inputAccountId; set { _inputAccountId = value; OnPropertyChanged(); } }
    public string InputApiToken { get => _inputApiToken; set { _inputApiToken = value; OnPropertyChanged(); } }
    public string InputAiModel { get => _inputAiModel; set { _inputAiModel = value; OnPropertyChanged(); } }
    public string InputChatModel { get => _inputChatModel; set { _inputChatModel = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
    public string ChatResponse { get => _chatResponse; set { _chatResponse = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        InputAccountId = _settings.CloudflareAccountId;
        InputApiToken = _settings.CloudflareApiToken;
        InputAiModel = _settings.AiModel;
        InputChatModel = _settings.ChatModel;

        if (!string.IsNullOrEmpty(_settings.CloudflareAccountId) && !string.IsNullOrEmpty(_settings.CloudflareApiToken))
            _aiService.Configure(_settings.CloudflareAccountId, _settings.CloudflareApiToken);

        if (!string.IsNullOrEmpty(_settings.LastOpenedFolder) && Directory.Exists(_settings.LastOpenedFolder))
            SelectedFolder = _settings.LastOpenedFolder;
    }

    public void NewFile()
    {
        var tab = new EditorTabViewModel { Title = "Без имени", Content = "" };
        Tabs.Add(tab);
        ActiveTab = tab;
        UpdateStatusBar();
    }

    public void OpenFile(string path)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == path);
        if (existing != null) { ActiveTab = existing; return; }

        var tab = new EditorTabViewModel { FilePath = path, Content = _fileService.ReadFile(path), IsModified = false };
        Tabs.Add(tab);
        ActiveTab = tab;
        UpdateStatusBar();
    }

    public void SaveActiveTab()
    {
        if (ActiveTab == null) return;
        if (string.IsNullOrEmpty(ActiveTab.FilePath)) return;
        _fileService.SaveFile(ActiveTab.FilePath, ActiveTab.Content);
        ActiveTab.IsModified = false;
        StatusBarText = $"Сохранено: {ActiveTab.Title}";
    }

    public void CloseTab(EditorTabViewModel tab)
    {
        Tabs.Remove(tab);
        ActiveTab = Tabs.LastOrDefault();
        UpdateStatusBar();
    }

    public void ToggleAiPanel()
    {
        IsAiPanelVisible = !IsAiPanelVisible;
        IsSettingsVisible = false;
    }

    public void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;
        IsAiPanelVisible = false;
    }

    public void SaveSettings()
    {
        _settings.CloudflareAccountId = InputAccountId;
        _settings.CloudflareApiToken = InputApiToken;
        _settings.AiModel = InputAiModel;
        _settings.ChatModel = InputChatModel;
        _settings.Save();
        _aiService.Configure(InputAccountId, InputApiToken);
        IsSettingsVisible = false;
        StatusBarText = "Настройки сохранены";
    }

    public async Task SendChatMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput) || !_aiService.IsConfigured) return;

        var userMessage = ChatInput;
        ChatInput = "";
        ChatHistory.Add(new ChatMessage { Role = "user", Content = userMessage });
        OnPropertyChanged(nameof(ChatHistory));
        ChatResponse = "";
        IsLoading = true;

        try
        {
            var history = ChatHistory.ToList();
            await foreach (var token in _aiService.ChatStreamAsync(userMessage, _settings.ChatModel, history))
            {
                ChatResponse += token;
                OnPropertyChanged(nameof(ChatResponse));
            }

            ChatHistory.Add(new ChatMessage { Role = "assistant", Content = ChatResponse });
            OnPropertyChanged(nameof(ChatHistory));
        }
        catch (Exception ex)
        {
            ChatResponse = $"Ошибка: {ex.Message}";
            OnPropertyChanged(nameof(ChatResponse));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void LoadFileTree()
    {
        FileTree.Clear();
        if (string.IsNullOrEmpty(SelectedFolder)) return;

        var tree = _fileService.GetDirectoryTree(SelectedFolder);
        foreach (var child in tree.Children)
            FileTree.Add(child);

        _settings.LastOpenedFolder = SelectedFolder;
        _settings.Save();
    }

    public void UpdateStatusBar()
    {
        StatusBarText = ActiveTab != null
            ? $"{ActiveTab.Language.ToUpperInvariant()} | {ActiveTab.Title}{(ActiveTab.IsModified ? " *" : "")}"
            : "MarkStudio";
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
