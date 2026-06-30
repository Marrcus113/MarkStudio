using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using MarkStudio.Models;
using MarkStudio.Services;
using TextMateSharp.Grammars;

namespace MarkStudio;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly GitService _git = new();
    private RegistryOptions? _registryOptions;
    private TextMate.Installation? _textMate;
    private readonly List<TabData> _tabs = new();
    private TabData? _activeTab;
    private string? _openFolder;
    private LocalLinter? _linter;
    private readonly List<ChatMessage> _chatHistory = new();
    private string _aiMode = "ask";
    private static readonly string[] ThemeNames = { "DarkPlus", "Monokai", "OneDark", "Dracula", "SolarizedDark" };

    private class TabData
    {
        public string FilePath = "";
        public string Title = "";
        public string Language = "plain";
        public bool Modified;
        public string Content = "";
    }

    private class ChatMessage
    {
        public string Role = "";
        public string Content = "";
    }

    public MainWindow()
    {
        InitializeComponent();
        _registryOptions = new RegistryOptions(ParseTheme(_settings.ThemeName));
        Placeholder.IsVisible = true;
        this.Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        SetAccountId.Text = _settings.CloudflareAccountId;
        SetApiToken.Text = _settings.CloudflareApiToken;
        SetAiModel.Text = _settings.AiModel;
        SetChatModel.Text = _settings.ChatModel;
        foreach (var name in ThemeNames) ThemeCombo.Items.Add(name);
        ThemeCombo.SelectedItem = _settings.ThemeName;
        ThemeCombo.SelectionChanged += (_, _) =>
        {
            _settings.ThemeName = ThemeCombo.SelectedItem?.ToString() ?? "Monokai";
            _registryOptions = new RegistryOptions(ParseTheme(_settings.ThemeName));
            if (_activeTab != null) SetupHighlighting(_activeTab.Language);
        };
        _registryOptions = new RegistryOptions(ParseTheme(_settings.ThemeName));
        if (_settings.CloudflareAccountId.Length > 0 && _settings.CloudflareApiToken.Length > 0)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.CloudflareApiToken);

        MenuNew.Click += async (_, _) => await NewTabAsync();
        MenuOpen.Click += (_, _) => OpenFile();
        MenuOpenFolder.Click += (_, _) => OpenFolder();
        MenuSave.Click += (_, _) => SaveTab();
        MenuSaveAs.Click += (_, _) => SaveAs();
        MenuToggleProject.Click += (_, _) => ToggleProject();
        MenuToggleTerminal.Click += (_, _) => ToggleBottomPanel();
        MenuToggleAi.Click += (_, _) => { ToggleBottomPanel(); ShowBottomTab("ai"); };
        ToolNew.Click += async (_, _) => await NewTabAsync();
        ToolOpen.Click += (_, _) => OpenFile();
        ToolSave.Click += (_, _) => SaveTab();
        ToolUndo.Click += (_, _) => { try { Editor.Undo(); } catch { } };
        ToolRedo.Click += (_, _) => { try { Editor.Redo(); } catch { } };
        ToolCommit.Click += (_, _) => _ = CommitChanges();
        TabProblems.Click += (_, _) => ShowBottomTab("problems");
        TabAiChat.Click += (_, _) => ShowBottomTab("ai");
        TabOutput.Click += (_, _) => ShowBottomTab("output");
        TabSettings.Click += (_, _) => ShowBottomTab("settings");
        BtnCloseBottom.Click += (_, _) => BottomPanel.IsVisible = false;
        BtnSend.Click += (_, _) => _ = SendAiChat();
        ChatInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _ = SendAiChat(); e.Handled = true; } };
        ModeAsk.Click += (_, _) => SetAiMode("ask");
        ModeCheck.Click += (_, _) => SetAiMode("check");
        ModeExplain.Click += (_, _) => SetAiMode("explain");
        ModeFix.Click += (_, _) => SetAiMode("fix");
        BtnSaveSettings.Click += (_, _) => SaveSettings();
        BtnOpenFolder.Click += (_, _) => OpenFolder();
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
        _linter = new LocalLinter(Editor, StatusBarLeft);

        var lastText = "";
        var pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        pollTimer.Tick += (_, _) =>
        {
            try
            {
                if (_activeTab == null || Editor.Document == null) return;
                var now = Editor.Document.Text;
                if (now != lastText)
                {
                    lastText = now;
                    _linter?.RunLinter();
                    UpdateMinimap();
                    UpdateStatusBar();
                }
            }
            catch { }
        };
        pollTimer.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (e.Key == Key.N && ctrl) { _ = NewTabAsync(); e.Handled = true; }
        else if (e.Key == Key.O && ctrl) { OpenFile(); e.Handled = true; }
        else if (e.Key == Key.S && ctrl) { SaveTab(); e.Handled = true; }
        else if (e.Key == Key.W && ctrl) { CloseActiveTab(); e.Handled = true; }
        else if (e.Key == Key.Z && ctrl) { try { Editor.Undo(); } catch { } e.Handled = true; }
        else if (e.Key == Key.Y && ctrl) { try { Editor.Redo(); } catch { } e.Handled = true; }
        else if (e.Key == Key.D1 && alt) { ToggleProject(); e.Handled = true; }
        else if (e.Key == Key.F12 && alt) { ToggleBottomPanel(); e.Handled = true; }
        else if (e.Key == Key.A && alt) { ToggleBottomPanel(); ShowBottomTab("ai"); e.Handled = true; }
    }

    private void SetAiMode(string mode)
    {
        _aiMode = mode;
        ModeAsk.Background = new SolidColorBrush(mode == "ask" ? Color.Parse("#4B6EAF") : Color.Parse("#3C3F41"));
        ModeAsk.Foreground = new SolidColorBrush(mode == "ask" ? Colors.White : Color.Parse("#BBBBBB"));
        ModeCheck.Background = new SolidColorBrush(mode == "check" ? Color.Parse("#4B6EAF") : Color.Parse("#3C3F41"));
        ModeCheck.Foreground = new SolidColorBrush(mode == "check" ? Colors.White : Color.Parse("#BBBBBB"));
        ModeExplain.Background = new SolidColorBrush(mode == "explain" ? Color.Parse("#4B6EAF") : Color.Parse("#3C3F41"));
        ModeExplain.Foreground = new SolidColorBrush(mode == "explain" ? Colors.White : Color.Parse("#BBBBBB"));
        ModeFix.Background = new SolidColorBrush(mode == "fix" ? Color.Parse("#4B6EAF") : Color.Parse("#3C3F41"));
        ModeFix.Foreground = new SolidColorBrush(mode == "fix" ? Colors.White : Color.Parse("#BBBBBB"));
    }

    private void ToggleProject()
    {
        var grid = TreeView.Parent as Grid;
        if (grid?.Parent is Border b) b.IsVisible = !b.IsVisible;
    }

    private void ToggleBottomPanel() { BottomPanel.IsVisible = !BottomPanel.IsVisible; }

    private void ShowBottomTab(string tab)
    {
        PanelProblems.IsVisible = tab == "problems";
        PanelAiChat.IsVisible = tab == "ai";
        PanelOutput.IsVisible = tab == "output";
        PanelSettings.IsVisible = tab == "settings";
        TabProblems.Foreground = new SolidColorBrush(tab == "problems" ? Colors.White : Color.Parse("#646464"));
        TabAiChat.Foreground = new SolidColorBrush(tab == "ai" ? Colors.White : Color.Parse("#646464"));
        TabOutput.Foreground = new SolidColorBrush(tab == "output" ? Colors.White : Color.Parse("#646464"));
        TabSettings.Foreground = new SolidColorBrush(tab == "settings" ? Colors.White : Color.Parse("#646464"));
    }

    private async Task NewTabAsync()
    {
        var (ext, lang) = await FileTypeDialog.ShowAsync(this);
        if (ext == null) return;
        var tab = new TabData { Title = "Без имени_" + (_tabs.Count + 1) + ext, Language = lang ?? "plain", Modified = true };
        _tabs.Add(tab);
        ActivateTab(tab);
    }

    private async void OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть файл", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Все файлы") { Patterns = new[] { "*.*" } } }
        });
        if (files.Count == 0) return;
        OpenFilePath(files[0].Path.LocalPath);
    }

    private void OpenFilePath(string path)
    {
        if (!File.Exists(path)) return;
        var existing = _tabs.FirstOrDefault(t => t.FilePath == path);
        if (existing != null) { ActivateTab(existing); return; }
        var tab = new TabData { FilePath = path, Title = Path.GetFileName(path), Language = DetectLang(path) };
        try { tab.Content = File.ReadAllText(path); } catch { }
        _tabs.Add(tab);
        ActivateTab(tab);
    }

    private async void OpenFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Открыть папку", AllowMultiple = false });
        if (folders.Count == 0) return;
        _openFolder = folders[0].Path.LocalPath;
        _settings.LastOpenedFolder = _openFolder;
        _settings.Save();
        _git.Open(_openFolder);
        UpdateGitBranch();
        BuildTree();
    }

    private void BuildTree()
    {
        TreeView.Items.Clear();
        if (string.IsNullOrEmpty(_openFolder) || !Directory.Exists(_openFolder)) return;
        foreach (var item in GetDirContent(_openFolder))
            TreeView.Items.Add(MakeTreeItem(item));
    }

    private List<(string Name, string FullPath, bool IsDir)> GetDirContent(string dir)
    {
        var result = new List<(string, string, bool)>();
        try
        {
            foreach (var d in Directory.GetDirectories(dir).Where(d =>
            { var n = Path.GetFileName(d); return !n.StartsWith('.') && n != "node_modules" && n != "__pycache__" && n != "bin" && n != "obj"; }))
                result.Add((Path.GetFileName(d), d, true));
            foreach (var f in Directory.GetFiles(dir).OrderBy(f => Path.GetFileName(f)))
                result.Add((Path.GetFileName(f), f, false));
        }
        catch { }
        return result;
    }

    private TreeViewItem MakeTreeItem((string Name, string FullPath, bool IsDir) item)
    {
        var icon = GetFileIcon(item.Name, item.IsDir);
        var gitStatus = GetGitFileStatus(item.FullPath, item.IsDir);
        var header = icon + " " + item.Name + gitStatus.StatusLabel;
        var foreground = gitStatus.Color;

        var tvItem = new TreeViewItem { Header = header, Foreground = new SolidColorBrush(Color.Parse(foreground)), Tag = item.FullPath };
        if (item.IsDir)
        {
            foreach (var child in GetDirContent(item.FullPath))
                tvItem.Items.Add(MakeTreeItem(child));
        }
        else
        {
            var path = item.FullPath;
            tvItem.DoubleTapped += (_, _) => OpenFilePath(path);
        }
        return tvItem;
    }

    private (string StatusLabel, string Color) GetGitFileStatus(string path, bool isDir)
    {
        if (!_git.IsRepoOpen || isDir) return ("", "#A9B7C6");
        var statuses = _git.GetFileStatuses(_openFolder ?? "");
        foreach (var s in statuses)
        {
            if (s.FilePath == path || s.FilePath.Replace('\\', '/') == path.Replace('\\', '/'))
            {
                return s.Status switch
                {
                    GitStatus.Modified => (" M", "#F0A830"),
                    GitStatus.Added => (" A", "#6A9955"),
                    GitStatus.Deleted => (" D", "#FF5555"),
                    GitStatus.Untracked => (" ?", "#6C6C6C"),
                    _ => ("", "#A9B7C6")
                };
            }
        }
        return ("", "#A9B7C6");
    }

    private void UpdateGitBranch()
    {
        try
        {
            ToolGitBranch.Text = _git.IsRepoOpen ? _git.CurrentBranch : "no git";
        }
        catch { ToolGitBranch.Text = "no git"; }
    }

    private async Task CommitChanges()
    {
        if (!_git.IsRepoOpen) { StatusBarLeft.Text = "Не git репозиторий"; return; }

        var dialog = new Window
        {
            Title = "Коммит", Width = 400, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#313335"))
        };

        var sp = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
        sp.Children.Add(new TextBlock { Text = "Сообщение коммита:", Foreground = new SolidColorBrush(Color.Parse("#A9B7C6")), FontSize = 12 });

        var msgBox = new TextBox { Background = new SolidColorBrush(Color.Parse("#3C3F41")), Foreground = new SolidColorBrush(Color.Parse("#A9B7C6")), BorderBrush = new SolidColorBrush(Color.Parse("#515151")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 4), FontSize = 12 };
        sp.Children.Add(msgBox);

        var statusText = new TextBlock { Text = "", Foreground = new SolidColorBrush(Color.Parse("#6A9955")), FontSize = 11 };
        sp.Children.Add(statusText);

        var okBtn = new Button
        {
            Content = "Коммит", Background = new SolidColorBrush(Color.Parse("#4B6EAF")),
            Foreground = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(12, 6), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left
        };
        okBtn.Click += (_, _) =>
        {
            var msg = msgBox.Text?.Trim();
            if (string.IsNullOrEmpty(msg)) { statusText.Text = "Введите сообщение!"; return; }
            var ok = _git.Commit(msg);
            statusText.Text = ok ? "Коммит создан!" : "Ошибка коммита";
            if (ok) dialog.Close();
        };
        sp.Children.Add(okBtn);

        dialog.Content = sp;
        await dialog.ShowDialog(this);
        UpdateGitBranch();
        BuildTree();
    }

    private static string GetFileIcon(string name, bool isDir)
    {
        if (isDir) return "\U0001F4C1";
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".py" => "\U0001F40D", ".js" or ".jsx" or ".mjs" or ".ts" or ".tsx" => "\U0001F4E6",
            ".cs" => "\U0001F48E", ".rs" => "\U0001F980", ".go" => "\U0001F439", ".java" => "\u2615",
            ".html" or ".htm" => "\U0001F310", ".css" or ".scss" or ".less" => "\U0001F3A8",
            ".json" or ".yaml" or ".yml" or ".toml" => "\u2699", ".md" => "\U0001F4DD",
            ".txt" => "\U0001F4C3", ".sh" or ".bash" => "\U0001F5A5",
            _ => "\U0001F4C4"
        };
    }

    private void ActivateTab(TabData tab)
    {
        if (_activeTab != null) _activeTab.Content = Editor.Document.Text;
        _activeTab = tab;
        Placeholder.IsVisible = false;
        Editor.Document = new TextDocument(tab.Content);
        Editor.CaretOffset = 0;
        SetupHighlighting(tab.Language);
        RefreshTabs();
        UpdateStatusBar();
        Dispatcher.UIThread.Post(() => Editor.Focus());
    }

    private void SetupHighlighting(string lang)
    {
        if (_registryOptions == null) return;
        var scope = lang switch
        {
            "csharp" => "source.cs", "javascript" or "jsx" => "source.js", "typescript" or "tsx" => "source.ts",
            "python" => "source.python", "json" => "source.json", "html" or "htm" => "text.html.basic",
            "css" => "source.css", "xml" or "csproj" or "axaml" or "xaml" => "text.xml",
            "yaml" or "yml" => "source.yaml", "shell" or "sh" => "source.shell",
            "c" or "h" => "source.c", "rust" => "source.rust", "go" => "source.go", "java" => "source.java",
            _ => ""
        };
        _textMate?.Dispose();
        _textMate = null;
        if (scope.Length > 0)
        {
            try { _textMate = Editor.InstallTextMate(_registryOptions); _textMate.SetGrammar(scope); } catch { }
        }
    }

    private void RefreshTabs()
    {
        TabsBar.Children.Clear();
        foreach (var tab in _tabs)
        {
            var isActive = ReferenceEquals(tab, _activeTab);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new TextBlock { Text = (tab.Modified ? "● " : "") + tab.Title, Foreground = new SolidColorBrush(isActive ? Colors.White : Color.Parse("#888")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var closeBtn = new Button { Content = "✕", FontSize = 9, Width = 16, Height = 16, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.Parse("#666")), BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            var t = tab; closeBtn.Click += (_, _) => CloseTab(t);
            sp.Children.Add(closeBtn);
            var border = new Border { Child = sp, Padding = new Thickness(12, 6), Background = new SolidColorBrush(isActive ? Color.Parse("#2B2B2B") : Color.Parse("#3C3F41")), BorderBrush = new SolidColorBrush(Color.Parse("#515151")), BorderThickness = new Thickness(0, 0, 1, isActive ? 2 : 0), Cursor = new Cursor(StandardCursorType.Hand) };
            var tabRef = tab; border.PointerPressed += (_, _) => ActivateTab(tabRef);
            TabsBar.Children.Add(border);
        }
    }

    private void CloseTab(TabData tab)
    {
        if (_activeTab == tab) _activeTab.Content = Editor.Document.Text;
        _tabs.Remove(tab);
        if (_activeTab == tab)
        {
            _activeTab = _tabs.LastOrDefault();
            if (_activeTab != null) { ActivateTab(_activeTab); return; }
            _activeTab = null;
            Editor.Document = new TextDocument("");
            Placeholder.IsVisible = true;
        }
        RefreshTabs();
        UpdateStatusBar();
    }

    private void CloseActiveTab() { if (_activeTab != null) CloseTab(_activeTab); }

    private void SaveTab()
    {
        if (_activeTab == null) return;
        _activeTab.Content = Editor.Document.Text;
        if (string.IsNullOrEmpty(_activeTab.FilePath)) { SaveAs(); return; }
        File.WriteAllText(_activeTab.FilePath, _activeTab.Content);
        _activeTab.Modified = false;
        RefreshTabs();
        StatusBarLeft.Text = "Сохранено: " + _activeTab.Title;
    }

    private async void SaveAs()
    {
        if (_activeTab == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Сохранить как", SuggestedFileName = _activeTab.Title });
        if (file == null) return;
        _activeTab.Content = Editor.Document.Text;
        File.WriteAllText(file.Path.LocalPath, _activeTab.Content);
        _activeTab.FilePath = file.Path.LocalPath;
        _activeTab.Title = Path.GetFileName(_activeTab.FilePath);
        _activeTab.Language = DetectLang(_activeTab.FilePath);
        _activeTab.Modified = false;
        SetupHighlighting(_activeTab.Language);
        RefreshTabs();
        UpdateStatusBar();
    }

    private void SaveSettings()
    {
        _settings.CloudflareAccountId = SetAccountId.Text ?? "";
        _settings.CloudflareApiToken = SetApiToken.Text ?? "";
        _settings.AiModel = SetAiModel.Text ?? "";
        _settings.ChatModel = SetChatModel.Text ?? "";
        _settings.Save();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.CloudflareApiToken);
        StatusBarLeft.Text = "Настройки сохранены!";
    }

    // ========== AI CHAT ==========

    private string GetSystemPrompt() => _aiMode switch
    {
        "check" => "Проанализируй код на ошибки. Покажи каждую ошибку с номером строки и объяснением. Отвечай на русском.",
        "explain" => "Объясни код пошагово простыми словами. Отвечай на русском.",
        "fix" => "Исправь все ошибки в коде. Верни исправленный код с объяснениями что было исправлено. Отвечай на русском.",
        _ => "Ты полезный помощник-программист. Отвечай кратко и по делу на русском языке."
    };

    private async Task SendAiChat()
    {
        var msg = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        if (_settings.CloudflareAccountId.Length == 0 || _settings.CloudflareApiToken.Length == 0)
        {
            AddChat("system", "Сначала настрой API! Перейди в Настройки и введи Cloudflare Account ID и Token.");
            ShowBottomTab("ai");
            BottomPanel.IsVisible = true;
            return;
        }

        ChatInput.Text = "";
        var finalMsg = msg;

        if (_activeTab != null && Editor.Document != null)
        {
            var code = Editor.Document.Text;
            if (!string.IsNullOrWhiteSpace(code))
            {
                finalMsg = _aiMode switch
                {
                    "check" => $"Проверь этот код на ошибки:\n```\n{code}\n```\n\nВопрос: {msg}",
                    "explain" => $"Объясни этот код:\n```\n{code}\n```\n\nВопрос: {msg}",
                    "fix" => $"Исправь ошибки в этом коде:\n```\n{code}\n```\n\nВопрос: {msg}",
                    _ => msg
                };
            }
        }

        AddChat("user", finalMsg);
        _chatHistory.Add(new ChatMessage { Role = "user", Content = finalMsg });

        var thinking = AddChat("assistant", "...");

        try
        {
            var messages = new List<object> { new { role = "system", content = GetSystemPrompt() } };
            foreach (var m in _chatHistory)
                messages.Add(new { role = m.Role, content = m.Content });

            var body = JsonSerializer.Serialize(new { model = _settings.ChatModel, messages, max_tokens = 2048, temperature = 0.3 });
            var url = $"https://api.cloudflare.com/client/v4/accounts/{_settings.CloudflareAccountId}/ai/v1/chat/completions";
            var response = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var reply = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "...";
                _chatHistory.Add(new ChatMessage { Role = "assistant", Content = reply });
                UpdateChatMessage(thinking, reply);
            }
            else UpdateChatMessage(thinking, "Ошибка: " + json[..Math.Min(300, json.Length)]);
        }
        catch (Exception ex) { UpdateChatMessage(thinking, "Ошибка: " + ex.Message); }
    }

    private Border AddChat(string role, string text)
    {
        var color = role switch { "user" => "#569CD6", "assistant" => "#6A9955", _ => "#CE9178" };
        var label = role switch { "user" => "Ты", "assistant" => "AI", _ => "Система" };
        var panel = new StackPanel { Margin = new Thickness(8, 4) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse(color)) });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#A9B7C6")), FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"), Margin = new Thickness(0, 2, 0, 0) });
        var border = new Border { Child = panel, Background = new SolidColorBrush(Color.Parse("#313335")), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6), Margin = new Thickness(6, 2), BorderBrush = new SolidColorBrush(Color.Parse("#515151")), BorderThickness = new Thickness(1) };
        ChatList.Items.Add(border);
        return border;
    }

    private void UpdateChatMessage(Border border, string newText)
    {
        try
        {
            if (border.Child is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
                tb.Text = newText;
        }
        catch { }
    }

    private void UpdateStatusBar()
    {
        if (_activeTab != null)
        {
            StatusBarLeft.Text = $"{_activeTab.Language.ToUpperInvariant()} | {_activeTab.Title}{(_activeTab.Modified ? " *" : "")}";
            StatusBarLang.Text = _activeTab.Language.ToUpperInvariant();
        }
        else { StatusBarLeft.Text = "MarkStudio"; StatusBarLang.Text = ""; }
        try
        {
            var offset = Math.Min(Editor.CaretOffset, Editor.Document?.TextLength ?? 0);
            if (Editor.Document != null && Editor.Document.TextLength > 0)
            {
                var line = Editor.Document.GetLineByOffset(offset);
                var col = offset - line.Offset + 1;
                StatusBarRight.Text = $"Ln {line.LineNumber}, Col {col}";
            }
            else StatusBarRight.Text = "Ln 1, Col 1";
        }
        catch { StatusBarRight.Text = "Ln 1, Col 1"; }
    }

    private void UpdateMinimap()
    {
        try
        {
            if (Editor.Document == null || Editor.Document.TextLength == 0) { MinimapText.Text = ""; return; }
            MinimapText.Text = Editor.Document.Text;
        }
        catch { }
    }

    private static string DetectLang(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp", ".js" or ".jsx" or ".mjs" => "javascript", ".ts" or ".tsx" => "typescript",
        ".py" => "python", ".json" => "json", ".html" or ".htm" => "html",
        ".css" or ".scss" or ".less" => "css", ".xml" or ".csproj" or ".axaml" or ".xaml" => "xml",
        ".md" => "markdown", ".yml" or ".yaml" => "yaml", ".sh" or ".bash" or ".zsh" => "shell",
        ".c" or ".h" or ".cpp" or ".hpp" => "c", ".rs" => "rust", ".go" => "go", ".java" => "java",
        _ => "plain"
    };

    private static ThemeName ParseTheme(string name) => name switch
    {
        "Monokai" => ThemeName.Monokai, "OneDark" => ThemeName.OneDark,
        "Dracula" => ThemeName.Dracula, "SolarizedDark" => ThemeName.SolarizedDark,
        _ => ThemeName.DarkPlus
    };
}
