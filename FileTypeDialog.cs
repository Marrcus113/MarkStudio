using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarkStudio;

public static class FileTypeDialog
{
    private static readonly (string Name, string Ext, string Scope)[] FileTypes =
    {
        ("Python (.py)",       ".py",   "python"),
        ("JavaScript (.js)",   ".js",   "javascript"),
        ("TypeScript (.ts)",   ".ts",   "typescript"),
        ("C# (.cs)",           ".cs",   "csharp"),
        ("Rust (.rs)",         ".rs",   "rust"),
        ("Go (.go)",           ".go",   "go"),
        ("Java (.java)",       ".java", "java"),
        ("HTML (.html)",       ".html", "html"),
        ("CSS (.css)",         ".css",  "css"),
        ("JSON (.json)",       ".json", "json"),
        ("XML (.xml)",         ".xml",  "xml"),
        ("YAML (.yml)",        ".yml",  "yaml"),
        ("C/C++ (.c/.h)",     ".c",    "c"),
        ("Shell (.sh)",        ".sh",   "shell"),
        ("Markdown (.md)",     ".md",   "markdown"),
        ("Текст (.txt)",       ".txt",  "txt"),
    };

    public static async Task<(string? Extension, string? Language)> ShowAsync(Window owner)
    {
        var tcs = new TaskCompletionSource<(string?, string?)>();

        var dialog = new Window
        {
            Title = "Новый файл",
            Width = 300, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            Foreground = new SolidColorBrush(Color.Parse("#CCC")),
            CanResize = false,
            Content = BuildContent(tcs)
        };

        _ = dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    private static StackPanel BuildContent(TaskCompletionSource<(string?, string?)> tcs)
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 6 };

        panel.Children.Add(new TextBlock
        {
            Text = "Выбери тип файла:",
            FontSize = 14, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var scroll = new ScrollViewer { MaxHeight = 340, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var list = new StackPanel { Spacing = 6 };

        foreach (var (name, ext, scope) in FileTypes)
        {
            var btn = new Button
            {
                Content = name,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
                Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
                BorderBrush = new SolidColorBrush(Color.Parse("#555")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6),
                FontSize = 13,
                Tag = (ext, scope)
            };
            btn.Click += (_, _) =>
            {
                tcs.TrySetResult((ext, scope));
                var parent = btn.Parent as Visual;
                while (parent != null)
                {
                    if (parent is Window w) { w.Close(); break; }
                    parent = parent.Parent as Visual;
                }
            };
            list.Children.Add(btn);
        }

        scroll.Content = list;
        panel.Children.Add(scroll);

        var cancelBtn = new Button
        {
            Content = "Отмена",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.Parse("#444")),
            Foreground = new SolidColorBrush(Color.Parse("#999")),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0)
        };
        cancelBtn.Click += (_, _) =>
        {
            tcs.TrySetResult((null, null));
            var parent = cancelBtn.Parent as Visual;
            while (parent != null)
            {
                if (parent is Window w) { w.Close(); break; }
                parent = parent.Parent as Visual;
            }
        };
        panel.Children.Add(cancelBtn);

        return panel;
    }
}
