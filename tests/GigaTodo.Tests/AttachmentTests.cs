using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using GigaTodo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using Xunit;

namespace GigaTodo.Tests;

public sealed class AttachmentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "giga-attachments-test-" + Guid.NewGuid().ToString("N"));
    private readonly ErrorLogger logger = new();
    private sealed class ErrorLogger : ILogger<AttachmentStore>
    {
        public Exception? Error;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Error = exception;
    }
    private AttachmentStore Store(AttachmentOptions? limits = null) => new(new Workspace(Options.Create(new RuntimeOptions { Root = root })), Options.Create(limits ?? new AttachmentOptions()), TimeProvider.System, logger);
    private static IFormFile File(string name, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name);
    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true)) foreach (var (name, data) in entries)
        { using var stream = archive.CreateEntry(name).Open(); stream.Write(data); }
        return buffer.ToArray();
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task ArbitraryBinaryAndEmptyFilesKeepExactContents()
    {
        var store = Store(); var bytes = new byte[] { 0, 255, 1, 2, 0, 42 };
        var a = await store.Save(File("program.exe", bytes), default);
        Assert.Equal("Ready", a.State); Assert.False(a.IsArchive);
        Assert.Equal(bytes, await System.IO.File.ReadAllBytesAsync(store.Download(a, null)));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), a.Sha256);
        var empty = await store.Save(File("empty.txt", []), default); Assert.Equal(0, empty.Size);
        Assert.Equal("program.exe", a.FileName);
    }

    [Fact]
    public async Task ZipAndNestedZipAreExtractedWithoutChangingOriginal()
    {
        var store = Store(); var inner = Zip(("deep/requirements.txt", Encoding.UTF8.GetBytes("Требования")));
        var original = Zip(("design.txt", Encoding.UTF8.GetBytes("Макет")), ("bundle.zip", inner));
        var a = await store.Save(File("input.zip", original), default);
        Assert.True(a.State == "Extracted", a.Error + " " + logger.Error); Assert.Equal(3, a.ExtractedFiles);
        Assert.Equal("Требования", await System.IO.File.ReadAllTextAsync(store.Download(a, "bundle.zip.contents/deep/requirements.txt")));
        Assert.Equal(original, await System.IO.File.ReadAllBytesAsync(store.Download(a, null)));
        Assert.Equal(3, store.Files(a).Length);
    }

    [Fact]
    public async Task TarGzipWithStandardDotPrefixAndUnicodeWorks()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
        using (var tar = new TarWriter(gzip, true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "./папка/данные.txt") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("tar content")) };
            tar.WriteEntry(entry);
        }
        var store = Store(); var a = await store.Save(File("data.tar.gz", output.ToArray()), default);
        Assert.True(a.State == "Extracted", a.Error + " " + logger.Error); Assert.Equal(1, a.ExtractedFiles);
        Assert.Equal("tar content", await System.IO.File.ReadAllTextAsync(store.Download(a, "папка/данные.txt")));
    }

    [Fact]
    public async Task SevenZipSolidArchiveWorks()
    {
        using var output = new MemoryStream();
        using (var archive = WriterFactory.OpenWriter(output, ArchiveType.SevenZip, new SevenZipWriterOptions { LeaveStreamOpen = true }))
        {
            archive.Write("input.txt", new MemoryStream(Encoding.UTF8.GetBytes("7z content")), DateTime.UtcNow);
        }
        var store = Store(); var a = await store.Save(File("bundle.7z", output.ToArray()), default);
        Assert.Equal("Extracted", a.State);
        Assert.Equal("7z content", await System.IO.File.ReadAllTextAsync(store.Download(a, "input.txt")));
    }

    [Fact]
    public async Task RarStoredEntryWorks()
    {
        // A tiny RAR4 fixture with one uncompressed entry; no external archiver.
        static uint Crc(byte[] bytes)
        {
            var crc = uint.MaxValue;
            foreach (var b in bytes) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            return ~crc;
        }
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        void Header(byte type, ushort flags, byte[] body)
        {
            using var header = new MemoryStream(); using var data = new BinaryWriter(header);
            data.Write(type); data.Write(flags); data.Write((ushort)(7 + body.Length)); data.Write(body);
            var bytes = header.ToArray(); writer.Write((ushort)Crc(bytes)); writer.Write(bytes);
        }
        writer.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00 }); Header(0x73, 0, new byte[6]);
        var name = Encoding.ASCII.GetBytes("input.txt"); var content = Encoding.UTF8.GetBytes("rar content");
        using var fileHeader = new MemoryStream(); using (var data = new BinaryWriter(fileHeader, Encoding.UTF8, true))
        {
            data.Write((uint)content.Length); data.Write((uint)content.Length); data.Write((byte)3);
            data.Write(Crc(content)); data.Write(0u); data.Write((byte)20); data.Write((byte)0x30);
            data.Write((ushort)name.Length); data.Write(0x81a4u); data.Write(name);
        }
        Header(0x74, 0x8000, fileHeader.ToArray()); writer.Write(content); Header(0x7b, 0, []);
        var store = Store(); var a = await store.Save(File("input.rar", output.ToArray()), default);
        Assert.True(a.State == "Extracted", a.Error + " " + logger.Error);
        Assert.Equal("rar content", await System.IO.File.ReadAllTextAsync(store.Download(a, "input.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/tmp/escape.txt")]
    [InlineData("C:\\escape.txt")]
    [InlineData("dir/../../escape.txt")]
    [InlineData("dir\\..\\escape.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("NUL")]
    public async Task UnsafeArchivePathsFailAndLeaveNoPartialExtraction(string path)
    {
        var store = Store(); var a = await store.Save(File("bad.zip", Zip(("valid.txt", [1]), (path, [2]))), default);
        Assert.Equal("Failed", a.State); Assert.NotNull(a.Error); Assert.False(Directory.Exists(store.Content(a)));
        Assert.True(System.IO.File.Exists(store.Source(a))); Assert.Empty(store.Files(a));
        Assert.Throws<RuleException>(() => AttachmentStore.EntryPath(root, path));
    }

    [Fact]
    public async Task LinksAndSpecialEntriesAreNotMaterialized()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xa1ff0000);
            using var stream = entry.Open(); stream.Write(Encoding.UTF8.GetBytes("../../outside"));
        }
        var store = Store(); var a = await store.Save(File("links.zip", buffer.ToArray()), default);
        Assert.Equal("Failed", a.State); Assert.False(Directory.Exists(store.Content(a)));
    }

    [Fact]
    public async Task SizeEntryAndDepthBudgetsApplyAcrossNestedArchives()
    {
        var bytes = Zip(("large.txt", new byte[2 * 1024 * 1024]));
        var size = await Store(new AttachmentOptions { MaxExpandedMegabytes = 1 }).Save(File("large.zip", bytes), default);
        Assert.Equal("Failed", size.State); Assert.Contains("размера", size.Error!);
        var entries = await Store(new AttachmentOptions { MaxArchiveEntries = 1 }).Save(File("many.zip", Zip(("a", [1]), ("b", [2]))), default);
        Assert.Equal("Failed", entries.State);
        var depth = await Store(new AttachmentOptions { MaxArchiveDepth = 1 }).Save(File("nested.zip", Zip(("inner.zip", Zip(("file", [1]))))), default);
        Assert.Equal("Failed", depth.State);
        Assert.False(Directory.Exists(Store().Content(size)));
    }

    [Fact]
    public async Task InvalidArchiveIsRetainedAndDownloadRejectsTraversalOrSymlinks()
    {
        var store = Store(); var bad = await store.Save(File("broken.rar", [1, 2, 3]), default);
        Assert.Equal("Failed", bad.State); Assert.True(System.IO.File.Exists(store.Download(bad, null)));
        var a = await store.Save(File("ok.zip", Zip(("readme.txt", [1]))), default);
        Assert.Throws<RuleException>(() => store.Download(a, "../original/file-ok.zip"));
        if (!OperatingSystem.IsWindows())
        {
            System.IO.File.CreateSymbolicLink(Path.Combine(store.Content(a), "link"), store.Source(a));
            Assert.Throws<RuleException>(() => store.Download(a, "link"));
        }
        await Assert.ThrowsAsync<RuleException>(() => Store(new AttachmentOptions { MaxUploadMegabytes = 1 }).Save(File("large.bin", new byte[2 * 1024 * 1024]), default));
    }
}
