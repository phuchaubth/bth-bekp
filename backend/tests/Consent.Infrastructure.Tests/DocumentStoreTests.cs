using System.Text;
using Consent.Application.Abstractions;
using Consent.Infrastructure.Security;
using Consent.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Tests;

public sealed class DocumentStoreTests : IDisposable
{
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());
    private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 Phạm Thị Dung (giả lập) 036188000125 %%EOF");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "consent-docs-" + Guid.NewGuid().ToString("N"));

    private EncryptedFileDocumentStore Store(string key = "") => new(
        Options.Create(new DocumentStorageOptions { DocumentsPath = _root }),
        Options.Create(new DataProtectionOptions { FieldEncryptionKey = key.Length > 0 ? key : KeyA, BlindIndexKey = KeyB }));

    private string FileOf(string key) => Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_File_IsEncrypted_AndReadsBackExactly()
    {
        var key = await Store().SaveAsync("PDT-2026-000001.pdf", Pdf, CancellationToken.None);

        Assert.Matches(@"^\d{4}/\d{2}/PDT-2026-000001-[0-9a-f]{32}\.pdf\.enc$", key);
        var onDisk = await File.ReadAllBytesAsync(FileOf(key));
        var text = Encoding.Latin1.GetString(onDisk);
        Assert.StartsWith("CDOC", text, StringComparison.Ordinal);
        Assert.DoesNotContain("%PDF", text, StringComparison.Ordinal);
        Assert.DoesNotContain("036188000125", text, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal(Pdf, await Store().ReadAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task ModifiedFile_IsDetected()
    {
        var key = await Store().SaveAsync("PDT-2026-000001.pdf", Pdf, CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(FileOf(key));
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(FileOf(key), bytes);

        await Assert.ThrowsAsync<DocumentUnavailableException>(() => Store().ReadAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task FileOfAnotherForm_CannotBeSwappedIn()
    {
        var store = Store();
        var first = await store.SaveAsync("PDT-2026-000001.pdf", Pdf, CancellationToken.None);
        var second = await store.SaveAsync("PDT-2026-000002.pdf", "%PDF khác"u8.ToArray(), CancellationToken.None);
        File.Copy(FileOf(first), FileOf(second), overwrite: true);

        await Assert.ThrowsAsync<DocumentUnavailableException>(() => store.ReadAsync(second, CancellationToken.None));
    }

    [Fact]
    public async Task WrongEncryptionKey_CannotRead()
    {
        var key = await Store().SaveAsync("PDT-2026-000001.pdf", Pdf, CancellationToken.None);
        var otherKey = Convert.ToBase64String(Enumerable.Range(50, 32).Select(i => (byte)i).ToArray());

        await Assert.ThrowsAsync<DocumentUnavailableException>(() => Store(otherKey).ReadAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("2026/11/../../../secret.pdf.enc")]
    [InlineData("2026/11/khong-ton-tai.pdf.enc")]
    public async Task InvalidOrMissingKeys_AreRejected(string key) =>
        await Assert.ThrowsAsync<DocumentUnavailableException>(() => Store().ReadAsync(key, CancellationToken.None));
}
