using System.Security.Cryptography;
using Consent.Application.Abstractions;
using Consent.Infrastructure.His;
using Consent.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Tests;

public sealed class DataProtectionTests
{
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    private static AesGcmFieldEncryptor Encryptor(string key) =>
        new(Options.Create(new DataProtectionOptions { FieldEncryptionKey = key, BlindIndexKey = KeyB }));

    private static HmacBlindIndexer Indexer(string key) =>
        new(Options.Create(new DataProtectionOptions { FieldEncryptionKey = KeyA, BlindIndexKey = key }));

    [Fact]
    public void Encrypt_RoundTrips_AndHidesPlaintext()
    {
        var encryptor = Encryptor(KeyA);

        var cipher = encryptor.Encrypt("001085000001");

        Assert.StartsWith("v1:", cipher, StringComparison.Ordinal);
        Assert.DoesNotContain("001085000001", cipher, StringComparison.Ordinal);
        Assert.Equal("001085000001", encryptor.Decrypt(cipher));
        Assert.Equal("Nguyễn Văn An", encryptor.Decrypt(encryptor.Encrypt("Nguyễn Văn An")));
    }

    [Fact]
    public void Encrypt_UsesRandomNonce_SoSameValueGivesDifferentCiphertext()
    {
        var encryptor = Encryptor(KeyA);

        Assert.NotEqual(encryptor.Encrypt("0900000001"), encryptor.Encrypt("0900000001"));
    }

    [Fact]
    public void Decrypt_DetectsTampering_AndWrongKey()
    {
        var cipher = Encryptor(KeyA).Encrypt("001085000001");
        var bytes = Convert.FromBase64String(cipher[3..]);
        bytes[^1] ^= 0x01;
        var tampered = "v1:" + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => Encryptor(KeyA).Decrypt(tampered));
        Assert.ThrowsAny<CryptographicException>(() => Encryptor(KeyB).Decrypt(cipher));
        Assert.ThrowsAny<CryptographicException>(() => Encryptor(KeyA).Decrypt("khong-phai-ban-ma"));
    }

    [Fact]
    public void BlindIndex_IsDeterministic_KeyDependent_AndIgnoresSurroundingSpaces()
    {
        var hash = Indexer(KeyB).Compute("001085000001");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, Indexer(KeyB).Compute(" 001085000001 "));
        Assert.NotEqual(hash, Indexer(KeyA).Compute("001085000001"));
        Assert.NotEqual(hash, Convert.ToHexString(SHA256.HashData("001085000001"u8)).ToLowerInvariant());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("khong-phai-base64!", false)]
    [InlineData("AQIDBA==", false)] // 4 byte
    public void IsValidKey_RejectsMissingOrShortKeys(string key, bool expected)
    {
        Assert.Equal(expected, DataProtectionOptions.IsValidKey(key));
        Assert.True(DataProtectionOptions.IsValidKey(KeyA));
    }
}

public sealed class FakeHisGatewayTests
{
    private static FakeHisGateway Gateway() => new(
        Options.Create(new HisGatewayOptions { FakeLatencyMilliseconds = 0 }),
        new FixedClock(),
        NullLogger<FakeHisGateway>.Instance);

    [Fact]
    public async Task EvenLastDigit_IsInsuredPatient_WithEncounter()
    {
        var result = await Gateway().FindPatientByCitizenIdAsync("079192000002", CancellationToken.None);

        Assert.Equal(HisLookupStatus.Found, result.Status);
        Assert.Equal("BN92000002", result.Patient!.PatientCode);
        Assert.Equal("BHYT", result.Patient.CoverageType);
        Assert.Matches("^[A-Z]{2}[0-9]{13}$", result.Patient.HealthInsuranceNumber!);
        Assert.NotNull(result.Patient.CurrentEncounter);
    }

    [Fact]
    public async Task OddLastDigit_IsServicePatient()
    {
        var result = await Gateway().FindPatientByCitizenIdAsync("001085000001", CancellationToken.None);

        Assert.Equal("Dịch vụ", result.Patient!.CoverageType);
        Assert.Null(result.Patient.HealthInsuranceNumber);
    }

    [Fact]
    public async Task LastDigitZero_IsNotFound_AndTripleNine_IsUnavailable()
    {
        Assert.Equal(HisLookupStatus.NotFound, (await Gateway().FindPatientByCitizenIdAsync("048210000010", CancellationToken.None)).Status);
        await Assert.ThrowsAsync<HttpRequestException>(() => Gateway().FindPatientByCitizenIdAsync("001085000999", CancellationToken.None));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    }
}
