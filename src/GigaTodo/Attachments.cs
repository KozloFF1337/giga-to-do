using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace GigaTodo;

public sealed class AttachmentOptions
{
    public int MaxUploadMegabytes { get; set; } = 100;
    public int MaxExpandedMegabytes { get; set; } = 512;
    public int MaxArchiveEntries { get; set; } = 10000;
    public int MaxArchiveDepth { get; set; } = 3;
    public int ExtractionTimeoutSeconds { get; set; } = 120;
    public long UploadBytes => (long)MaxUploadMegabytes * 1024 * 1024;
    public long ExpandedBytes => (long)MaxExpandedMegabytes * 1024 * 1024;
    public static bool Valid(AttachmentOptions o) => o.MaxUploadMegabytes is >= 1 and <= 1024
        && o.MaxExpandedMegabytes is >= 1 and <= 10240 && o.MaxArchiveEntries is >= 1 and <= 100000
        && o.MaxArchiveDepth is >= 1 and <= 10 && o.ExtractionTimeoutSeconds is >= 1 and <= 600;
}

public sealed record AttachmentInfo(Guid Id, string FileName, long Size, string Sha256, bool IsArchive, string State, string? Error,
    int ExtractedFiles, long ExtractedBytes, DateTimeOffset CreatedAt, string OriginalPath, string? ContentPath);
public sealed record AttachedFile(string Path, long Size);

public sealed class AttachmentStore(Workspace workspace, IOptions<AttachmentOptions> options, TimeProvider clock, ILogger<AttachmentStore>? logger = null)
{
    public AttachmentOptions Limits => options.Value;
    public string Root => Path.Combine(workspace.Root, "attachments");
    private string Folder(Guid id) => Path.Combine(Root, id.ToString("N"));
    public string Source(Attachment a) => Path.Combine(Folder(a.Id), "original", "file-" + a.FileName);
    public string Content(Attachment a) => Path.Combine(Folder(a.Id), "content");
    public AttachmentInfo Describe(Attachment a) => new(a.Id, a.FileName, a.Size, a.Sha256, a.IsArchive, a.State, a.Error,
        a.ExtractedFiles, a.ExtractedBytes, a.CreatedAt, Source(a), a.State == "Extracted" ? Content(a) : null);
    public async Task<AttachmentInfo[]> Describe(Database db, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var items = await db.Attachments.AsNoTracking().Where(a => ids.Contains(a.Id)).ToArrayAsync(ct);
        return ids.Distinct().Select(id => items.FirstOrDefault(a => a.Id == id)).OfType<Attachment>().Select(Describe).ToArray();
    }

    public static string FileName(string name)
    {
        var value = name.Replace('\\', '/').Split('/').Last();
        value = new string(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (value.Length > 180) value = value[..150] + Path.GetExtension(value)[..Math.Min(Path.GetExtension(value).Length, 20)];
        if (value.Length == 0 || value is "." or "..") value = "file";
        return value;
    }

    public async Task<Attachment> Save(IFormFile file, CancellationToken ct)
    {
        if (file.Length > Limits.UploadBytes) throw new RuleException($"Файл превышает лимит {Limits.MaxUploadMegabytes} МБ", 413);
        var a = new Attachment { FileName = FileName(file.FileName), CreatedAt = clock.GetUtcNow() };
        var folder = Folder(a.Id);
        Directory.CreateDirectory(Path.Combine(folder, "original"));
        EnsureNoLinks(folder);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(Source(a), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    a.Size += count;
                    if (a.Size > Limits.UploadBytes) throw new RuleException("Превышен лимит загрузки", 413);
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            a.Sha256 = Convert.ToHexString(hash.GetHashAndReset());
            if (LooksLikeArchive(Source(a)))
            {
                a.IsArchive = true;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(Limits.ExtractionTimeoutSeconds));
                try
                {
                    var budget = new ExpansionBudget();
                    await Expand(Source(a), Content(a), 1, budget, deadline.Token);
                    a.State = "Extracted"; a.ExtractedFiles = budget.Files; a.ExtractedBytes = budget.Bytes;
                }
                catch (Exception error) when (!ct.IsCancellationRequested && error is not OutOfMemoryException)
                {
                    logger?.LogWarning(error, "Не удалось распаковать вложение {Attachment}", a.Id);
                    if (Directory.Exists(Content(a))) Directory.Delete(Content(a), true);
                    a.State = "Failed";
                    a.Error = error is OperationCanceledException ? "Превышено время распаковки" : error is RuleException
                        ? error.Message : "Архив не удалось распаковать: он повреждён, зашифрован, требует недостающие тома или использует неподдерживаемый вариант формата.";
                }
            }
            return a;
        }
        catch { if (Directory.Exists(folder)) Directory.Delete(folder, true); throw; }
    }

    private sealed class ExpansionBudget { public long Bytes; public int Entries; public int Files; }
    private async Task Expand(string source, string destination, int depth, ExpansionBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > Limits.MaxArchiveDepth) throw new RuleException("Превышена допустимая вложенность архивов");
        Directory.CreateDirectory(destination);
        // 7z needs random access/solid sequential extraction. ReaderFactory also
        // handles compressed TAR wrappers and standalone gzip/bzip2/xz streams.
        using var archive = ArchiveFactory.IsArchive(source, out var type) && type is ArchiveType.SevenZip or ArchiveType.Zip ? ArchiveFactory.OpenArchive(source) : null;
        if (type == ArchiveType.Zip && archive is not null)
        {
            var count = 0;
            // ZIP local headers omit external attributes; inspect the central
            // directory before opening its forward-only extraction stream.
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (++count > Limits.MaxArchiveEntries) throw new RuleException("Превышено число элементов архива");
                CheckEntry(entry);
            }
        }
        using var reader = archive is not null && type == ArchiveType.SevenZip ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(source);
        var nested = new List<string>();
        try
        {
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (++budget.Entries > Limits.MaxArchiveEntries) throw new RuleException("Превышено число элементов архива");
                var entry = reader.Entry;
                CheckEntry(entry);
                var key = entry.Key;
                if (string.IsNullOrWhiteSpace(key)) key = Path.GetFileNameWithoutExtension(source) + ".unpacked";
                if (entry.IsDirectory && key.Replace('\\', '/').TrimEnd('/') == ".") continue;
                var path = EntryPath(destination, key);
                if (entry.IsDirectory) { Directory.CreateDirectory(path); continue; }
                if (entry.Size > Limits.ExpandedBytes - budget.Bytes) throw new RuleException("Превышен лимит распакованного размера");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Never use archive helpers that create links, overwrite files,
                // restore executable bits, or trust archive-supplied absolute paths.
                using var input = reader.OpenEntryStream();
                try
                {
                    await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    var buffer = new byte[65536]; int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        budget.Bytes += count;
                        if (budget.Bytes > Limits.ExpandedBytes) throw new RuleException("Превышен лимит распакованного размера");
                        await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    }
                }
                catch { reader.Cancel(); throw; }
                budget.Files++;
                if (LooksLikeArchive(path)) nested.Add(path);
            }
        }
        catch { reader.Cancel(); throw; }
        foreach (var path in nested) await Expand(path, path + ".contents", depth + 1, budget, ct);
    }

    private static void CheckEntry(IEntry entry)
    {
        if (entry.IsEncrypted) throw new RuleException("Архив защищён паролем. Прикрепите незашифрованный архив.");
        if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new RuleException("Архив содержит символическую или жёсткую ссылку");
        int attributes;
        // Some forward-only entry types do not implement attributes. ZIP links
        // were checked from central-directory entries above; TAR has LinkTarget.
        try { attributes = entry.Attrib ?? 0; }
        catch (NotImplementedException) { attributes = 0; }
        var unixType = (attributes >> 16) & 0xf000;
        if (unixType == 0) unixType = attributes & 0xf000;
        if (unixType is not (0 or 0x8000 or 0x4000)) throw new RuleException("Архив содержит ссылку или специальный файл");
    }

    public static string EntryPath(string root, string key)
    {
        var normalized = key.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Any(char.IsControl)) throw new RuleException("Недопустимый путь в архиве");
        var pieces = normalized.TrimEnd('/').Split('/');
        if (pieces.Length > 40 || normalized.Length > 1000 || pieces.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new RuleException("Недопустимый путь в архиве");
        foreach (var piece in pieces)
        {
            var stem = piece.Split('.')[0].ToUpperInvariant();
            if (piece.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9'))
                throw new RuleException("Недопустимое имя элемента архива");
        }
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(pieces)));
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new RuleException("Путь выходит за папку архива");
        return path;
    }

    private static bool LooksLikeArchive(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2", ".xz", ".txz", ".lz", ".lzip", ".lzma", ".z", ".zst", ".zstd", ".cab", ".iso", ".arj", ".ace", ".arc" }.Contains(extension)) return true;
        using var stream = File.OpenRead(path); Span<byte> bytes = stackalloc byte[512]; var count = stream.Read(bytes);
        return count >= 4 && ((bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] is 3 or 5 or 7)
            || (bytes[0] == 0x1f && bytes[1] == 0x8b) || bytes[..4].SequenceEqual("Rar!"u8)
            || (bytes[0] == '7' && bytes[1] == 'z' && bytes[2] == 0xbc)
            || (bytes[0] == 'B' && bytes[1] == 'Z' && bytes[2] == 'h')
            || (bytes[0] == 0xfd && bytes[1] == '7' && bytes[2] == 'z')
            || (count > 262 && bytes.Slice(257, 5).SequenceEqual("ustar"u8)));
    }

    private void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path); var root = Path.GetFullPath(Root);
        while (current.Length >= root.Length)
        {
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new RuleException("Ссылки в хранилище вложений запрещены", 403);
            if (current == root) break;
            current = Path.GetDirectoryName(current)!;
        }
    }
    public string Download(Attachment a, string? relative)
    {
        var path = relative is null ? Source(a) : a.State == "Extracted" ? EntryPath(Content(a), relative) : throw new RuleException("Распаковка недоступна", 409);
        EnsureNoLinks(path);
        if (!File.Exists(path)) throw new RuleException("Файл вложения не найден", 404);
        return path;
    }
    public AttachedFile[] Files(Attachment a)
    {
        if (a.State != "Extracted") return [];
        EnsureNoLinks(Content(a));
        return Directory.EnumerateFiles(Content(a), "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Take(Limits.MaxArchiveEntries + 1).Select(path => new AttachedFile(Path.GetRelativePath(Content(a), path).Replace('\\', '/'), new FileInfo(path).Length)).ToArray();
    }
}
