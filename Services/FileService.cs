using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MarkStudio.Services;

public class FileItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public List<FileItem> Children { get; set; } = new();
}

public class FileService
{
    private static readonly string[] IgnoredDirs = { ".git", "node_modules", "bin", "obj", ".vs", ".idea" };

    public FileItem GetDirectoryTree(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var root = new FileItem
        {
            Name = dirInfo.Name,
            FullPath = dirInfo.FullName,
            IsDirectory = true
        };

        try
        {
            foreach (var dir in dirInfo.GetDirectories().Where(d => !IgnoredDirs.Contains(d.Name)))
            {
                root.Children.Add(GetDirectoryTree(dir.FullName));
            }
            foreach (var file in dirInfo.GetFiles())
            {
                root.Children.Add(new FileItem
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false
                });
            }
        }
        catch { }

        return root;
    }

    public string ReadFile(string path)
    {
        return File.ReadAllText(path);
    }

    public void SaveFile(string path, string content)
    {
        File.WriteAllText(path, content);
    }
}
