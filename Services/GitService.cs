using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;

namespace MarkStudio.Services;

public class GitFileStatus
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public GitStatus Status { get; set; }
}

public enum GitStatus
{
    Normal,
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked
}

public class GitService : IDisposable
{
    private Repository? _repo;
    private string? _repoPath;

    public bool IsRepoOpen => _repo != null;
    public string CurrentBranch => _repo?.Head?.FriendlyName ?? "";

    public void Open(string folderPath)
    {
        Close();
        try
        {
            var gitPath = Repository.Discover(folderPath);
            if (gitPath != null)
            {
                _repo = new Repository(gitPath);
                _repoPath = gitPath;
            }
        }
        catch { }
    }

    public void Close()
    {
        _repo?.Dispose();
        _repo = null;
        _repoPath = null;
    }

    public List<GitFileStatus> GetFileStatuses(string rootPath)
    {
        var result = new List<GitFileStatus>();
        if (_repo == null) return result;

        try
        {
            var statuses = _repo.RetrieveStatus();
            foreach (var entry in statuses)
            {
                var fullPath = Path.GetFullPath(entry.FilePath);
                var status = entry.State switch
                {
                    FileStatus.ModifiedInIndex => GitStatus.Modified,
                    FileStatus.ModifiedInWorkdir => GitStatus.Modified,
                    FileStatus.NewInIndex => GitStatus.Added,
                    FileStatus.NewInWorkdir => GitStatus.Untracked,
                    FileStatus.DeletedFromIndex => GitStatus.Deleted,
                    FileStatus.DeletedFromWorkdir => GitStatus.Deleted,
                    FileStatus.RenamedInIndex => GitStatus.Renamed,
                    FileStatus.RenamedInWorkdir => GitStatus.Renamed,
                    _ => GitStatus.Normal
                };

                if (status != GitStatus.Normal)
                {
                    result.Add(new GitFileStatus
                    {
                        FilePath = fullPath,
                        FileName = Path.GetFileName(entry.FilePath),
                        Status = status
                    });
                }
            }
        }
        catch { }

        return result;
    }

    public bool Commit(string message, string authorName = "Mark", string authorEmail = "mark@markstudio.dev")
    {
        if (_repo == null) return false;
        try
        {
            var signature = new Signature(authorName, authorEmail, DateTimeOffset.Now);
            var commit = _repo.Commit(message, signature, signature);
            return true;
        }
        catch { return false; }
    }

    public string GetDiff(string filePath)
    {
        if (_repo == null) return "";
        try
        {
            var relativePath = GetRelativePath(filePath);
            if (string.IsNullOrEmpty(relativePath)) return "";

            var tree = _repo.Head?.Tip?.Tree;
            if (tree == null) return "";

            var patch = _repo.Diff.Compare<Patch>(tree, DiffTargets.WorkingDirectory);
            return patch.Content;
        }
        catch { }
        return "";
    }

    public List<string> GetBranches()
    {
        var result = new List<string>();
        if (_repo == null) return result;
        try
        {
            foreach (var branch in _repo.Branches)
                result.Add(branch.FriendlyName);
        }
        catch { }
        return result;
    }

    public bool CreateBranch(string name)
    {
        if (_repo == null) return false;
        try
        {
            _repo.CreateBranch(name);
            return true;
        }
        catch { return false; }
    }

    public bool Checkout(string branchName)
    {
        if (_repo == null) return false;
        try
        {
            var branch = _repo.Branches[branchName];
            if (branch != null)
            {
                Commands.Checkout(_repo, branch);
                return true;
            }
        }
        catch { }
        return false;
    }

    public int GetCommitCount()
    {
        if (_repo == null) return 0;
        try { return _repo.Head.Commits.Count(); }
        catch { return 0; }
    }

    private string GetRelativePath(string fullPath)
    {
        if (_repoPath == null || _repo?.Info?.WorkingDirectory == null) return "";
        try
        {
            var repoRoot = _repo.Info.WorkingDirectory;
            if (fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath[repoRoot.Length..].Replace('\\', '/');
        }
        catch { }
        return "";
    }

    public void Dispose()
    {
        Close();
    }
}
