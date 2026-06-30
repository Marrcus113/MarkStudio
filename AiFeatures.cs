using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace MarkStudio;

public class LocalLinter
{
    private readonly TextEditor _editor;
    private readonly TextBlock _statusBar;
    public List<LinterError> Errors { get; } = new();

    public LocalLinter(TextEditor editor, TextBlock statusBar)
    {
        _editor = editor;
        _statusBar = statusBar;
    }

    public void RunLinter()
    {
        Errors.Clear();
        try
        {
            if (_editor.Document == null || _editor.Document.TextLength == 0) return;
            var lines = _editor.Document.Text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var n = i + 1;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var t = line.TrimStart();

                if (line.StartsWith('\t') && line.Contains("    "))
                    Errors.Add(new LinterError { Line = n, Message = "Смешаны табы и пробелы", Severity = LintSeverity.Warning });

                if ((t.StartsWith("def ") || t.StartsWith("class ") || t.StartsWith("if ") || t.StartsWith("elif ") ||
                     t.StartsWith("else") || t.StartsWith("for ") || t.StartsWith("while ") ||
                     t.StartsWith("try") || t.StartsWith("except ") || t.StartsWith("finally") || t.StartsWith("with "))
                    && !line.TrimEnd().EndsWith(':') && !line.TrimEnd().EndsWith('\\') && !line.Contains('#'))
                    Errors.Add(new LinterError { Line = n, Message = "Пропущено двоеточие ':'", Severity = LintSeverity.Error });

                if (t.StartsWith("if ") && line.Contains('=') && !line.Contains("==") && !line.Contains("!=") && !line.Contains("<=") && !line.Contains(">="))
                    Errors.Add(new LinterError { Line = n, Message = "Возможно '=' вместо '=='", Severity = LintSeverity.Error });

                int openP = line.Split('(').Length - 1;
                int closeP = line.Split(')').Length - 1;
                int openB = line.Split('[').Length - 1;
                int closeB = line.Split(']').Length - 1;
                if (openP != closeP)
                    Errors.Add(new LinterError { Line = n, Message = "Несбалансированные ()", Severity = LintSeverity.Warning });
                if (openB != closeB)
                    Errors.Add(new LinterError { Line = n, Message = "Несбалансированные []", Severity = LintSeverity.Warning });
            }
        }
        catch { }

        try
        {
            var errs = Errors.Count(e => e.Severity == LintSeverity.Error);
            var warns = Errors.Count(e => e.Severity == LintSeverity.Warning);
            if (Errors.Count == 0)
                _statusBar.Text += " | OK";
            else
            {
                var p = new List<string>();
                if (errs > 0) p.Add($"{errs} ошибок");
                if (warns > 0) p.Add($"{warns} предупреждений");
                _statusBar.Text += " | " + string.Join(", ", p);
            }
        }
        catch { }
    }
}

public class LinterError
{
    public int Line { get; set; }
    public string Message { get; set; } = "";
    public LintSeverity Severity { get; set; }
}

public enum LintSeverity { Error, Warning, Info }
