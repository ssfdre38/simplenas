using System.Text;

namespace SimpleNAS;

public record FileBrowserItem(
    string Name,
    string RelativePath,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    string SizeFormatted,
    DateTime LastModified,
    string Extension,
    string TypeCategory // "folder", "image", "video", "audio", "code", "archive", "document"
);

public record FileBrowserListing(
    string CurrentPath,
    string ParentPath,
    List<string> Breadcrumbs,
    List<FileBrowserItem> Items,
    long TotalFiles,
    long TotalFolders,
    string TotalSizeFormatted
);

public static class FileManagerService
{
    public static List<string> GetRootPaths()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    roots.Add(drive.Name);
                }
            }
        }
        else
        {
            roots.Add("/storage");
            if (Directory.Exists("/mnt")) roots.Add("/mnt");
            if (Directory.Exists("/media")) roots.Add("/media");
            roots.Add("/");
        }
        return roots;
    }

    public static string NormalizeDirectory(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            var roots = GetRootPaths();
            return roots.Count > 0 ? roots[0] : (OperatingSystem.IsWindows() ? @"C:\" : "/");
        }

        var normalized = Path.GetFullPath(requestedPath);
        if (!Directory.Exists(normalized))
        {
            var roots = GetRootPaths();
            return roots.Count > 0 ? roots[0] : (OperatingSystem.IsWindows() ? @"C:\" : "/");
        }
        return normalized;
    }

    public static FileBrowserListing ListDirectory(string? path)
    {
        var currentDir = NormalizeDirectory(path);
        var dirInfo = new DirectoryInfo(currentDir);

        string? parent = dirInfo.Parent?.FullName;
        if (parent == null && OperatingSystem.IsWindows() && !currentDir.EndsWith("\\"))
        {
            parent = Path.GetPathRoot(currentDir);
        }

        var breadcrumbs = new List<string>();
        var parts = currentDir.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        breadcrumbs.AddRange(parts);

        var items = new List<FileBrowserItem>();
        long totalSize = 0;
        long fileCount = 0;
        long dirCount = 0;

        try
        {
            // Enumerate Subdirectories
            foreach (var sub in dirInfo.GetDirectories())
            {
                // Skip system/hidden protected files
                if ((sub.Attributes & FileAttributes.Hidden) != 0 || (sub.Attributes & FileAttributes.System) != 0)
                {
                    if (sub.Name.StartsWith("$") || sub.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                items.Add(new FileBrowserItem(
                    Name: sub.Name,
                    RelativePath: Path.GetRelativePath(currentDir, sub.FullName),
                    FullPath: sub.FullName,
                    IsDirectory: true,
                    SizeBytes: 0,
                    SizeFormatted: "--",
                    LastModified: sub.LastWriteTimeUtc,
                    Extension: "",
                    TypeCategory: "folder"
                ));
                dirCount++;
            }

            // Enumerate Files
            foreach (var file in dirInfo.GetFiles())
            {
                if ((file.Attributes & FileAttributes.Hidden) != 0 && file.Name.StartsWith("$")) continue;

                var ext = file.Extension.ToLowerInvariant();
                items.Add(new FileBrowserItem(
                    Name: file.Name,
                    RelativePath: Path.GetRelativePath(currentDir, file.FullName),
                    FullPath: file.FullName,
                    IsDirectory: false,
                    SizeBytes: file.Length,
                    SizeFormatted: FormatBytes(file.Length),
                    LastModified: file.LastWriteTimeUtc,
                    Extension: ext,
                    TypeCategory: GetCategoryForExtension(ext)
                ));
                totalSize += file.Length;
                fileCount++;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Gracefully handle locked or restricted folders
        }
        catch (Exception)
        {
            // Directory read error
        }

        // Sort folders first alphabetically, then files
        items = items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return new FileBrowserListing(
            CurrentPath: currentDir,
            ParentPath: parent ?? "",
            Breadcrumbs: breadcrumbs,
            Items: items,
            TotalFiles: fileCount,
            TotalFolders: dirCount,
            TotalSizeFormatted: FormatBytes(totalSize)
        );
    }

    public static async Task UploadFilesAsync(string destinationDir, IFormFileCollection files)
    {
        var targetPath = NormalizeDirectory(destinationDir);
        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
        }

        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            var safeFileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(targetPath, safeFileName);

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await file.CopyToAsync(stream);
        }
    }

    public static void CreateDirectory(string parentDir, string newFolderName)
    {
        var parent = NormalizeDirectory(parentDir);
        var invalid = Path.GetInvalidFileNameChars();
        var cleanName = new string(newFolderName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleanName)) throw new ArgumentException("Invalid directory name.");

        var fullPath = Path.Combine(parent, cleanName);
        Directory.CreateDirectory(fullPath);
    }

    public static void DeleteItem(string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive: true);
        }
        else if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
        else
        {
            throw new FileNotFoundException("Path not found: " + targetPath);
        }
    }

    public static void RenameItem(string targetPath, string newName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleanName = new string(newName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleanName)) throw new ArgumentException("Invalid new name.");

        if (Directory.Exists(targetPath))
        {
            var parent = Path.GetDirectoryName(targetPath);
            if (parent != null)
            {
                var destination = Path.Combine(parent, cleanName);
                Directory.Move(targetPath, destination);
            }
        }
        else if (File.Exists(targetPath))
        {
            var parent = Path.GetDirectoryName(targetPath);
            if (parent != null)
            {
                var destination = Path.Combine(parent, cleanName);
                File.Move(targetPath, destination);
            }
        }
        else
        {
            throw new FileNotFoundException("Target not found: " + targetPath);
        }
    }

    public static async Task<string> ReadTextPreviewAsync(string filePath, int maxBytes = 64 * 1024)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found.");

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        var buffer = new byte[Math.Min(stream.Length, maxBytes)];
        int read = await stream.ReadAsync(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    public static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".txt" or ".log" or ".conf" or ".sh" or ".cs" or ".md" => "text/plain",
            ".html" => "text/html",
            _ => "application/octet-stream"
        };
    }

    private static string GetCategoryForExtension(string ext)
    {
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" or ".ico" => "image",
            ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" => "video",
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a" => "audio",
            ".zip" or ".tar" or ".gz" or ".rar" or ".7z" or ".iso" => "archive",
            ".cs" or ".js" or ".ts" or ".py" or ".html" or ".css" or ".json" or ".xml" or ".yml" or ".yaml" or ".sh" or ".ps1" => "code",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" => "document",
            _ => "file"
        };
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
