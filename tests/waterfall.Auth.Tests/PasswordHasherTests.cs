using Waterfall.Api.Services;
using Waterfall.Services.Interfaces;

namespace Waterfall.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="AspNetPasswordHasher"/> (PBKDF2; SEC-001).
/// </summary>
public sealed class PasswordHasherTests
{
    private readonly AspNetPasswordHasher _hasher = new();

    [Fact]
    public void HashVerify_Roundtrip_Succeeds()
    {
        string hash = _hasher.Hash("Secret123456");

        Assert.Equal(PasswordVerifyResult.Success, _hasher.Verify(hash, "Secret123456"));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        string hash = _hasher.Hash("Secret123456");

        Assert.Equal(PasswordVerifyResult.Failed, _hasher.Verify(hash, "Wrong99999999"));
    }

    [Fact]
    public void Verify_NullHash_FailsWithoutThrowing()
    {
        // Unknown-user branch: dummy work against the static stub, always Failed (THR-01).
        Assert.Equal(PasswordVerifyResult.Failed, _hasher.Verify(null, "Secret123456"));
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        // Random per-hash salt: never store or compare hashes directly.
        Assert.NotEqual(_hasher.Hash("Secret123456"), _hasher.Hash("Secret123456"));
    }

    [Fact]
    public void Hash_DoesNotContainPlaintext()
    {
        const string password = "Secret123456";
        Assert.DoesNotContain(password, _hasher.Hash(password), StringComparison.Ordinal);
    }
}
