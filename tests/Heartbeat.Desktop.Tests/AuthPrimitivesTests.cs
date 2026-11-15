using System.IO;
using System.Security.Cryptography;
using System.Text;

using Heartbeat.Desktop.Services.Auth;

using Microsoft.Extensions.Configuration;

namespace Heartbeat.Desktop.Tests;

public sealed class LocalPasswordHasherTests
{
    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        (string hash, string salt) = LocalPasswordHasher.Hash("clave-1");

        Assert.True(LocalPasswordHasher.Verify("clave-1", hash, salt));
    }

    [Theory]
    [InlineData("clave-2")]
    [InlineData("CLAVE-1")]
    [InlineData(" clave-1")]
    [InlineData("")]
    public void A_different_password_does_not_verify(string other)
    {
        (string hash, string salt) = LocalPasswordHasher.Hash("clave-1");

        Assert.False(LocalPasswordHasher.Verify(other, hash, salt));
    }

    [Fact]
    public void The_same_password_gets_a_different_salt_and_hash_each_time()
    {
        (string hash1, string salt1) = LocalPasswordHasher.Hash("clave-1");
        (string hash2, string salt2) = LocalPasswordHasher.Hash("clave-1");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void The_hash_records_its_iteration_count_and_hides_the_password()
    {
        (string hash, string salt) = LocalPasswordHasher.Hash("clave-1");

        Assert.StartsWith($"{LocalPasswordHasher.Iterations}.", hash);
        Assert.Equal(16, Convert.FromBase64String(salt).Length);
        Assert.Equal(32, Convert.FromBase64String(hash[(hash.IndexOf('.') + 1)..]).Length);
        Assert.DoesNotContain("clave-1", hash + salt);
    }

    [Fact]
    public void A_hash_made_with_other_iterations_still_verifies()
    {
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        byte[] raw = Rfc2898DeriveBytes.Pbkdf2("vieja", salt, 1000, HashAlgorithmName.SHA256, 32);
        string stored = "1000." + Convert.ToBase64String(raw);

        Assert.True(LocalPasswordHasher.Verify("vieja", stored, Convert.ToBase64String(salt)));
        Assert.False(LocalPasswordHasher.Verify("otra", stored, Convert.ToBase64String(salt)));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("sin-punto", "AAAA")]
    [InlineData(".AAAA", "AAAA")]
    [InlineData("0.AAAA", "AAAA")]
    [InlineData("-5.AAAA", "AAAA")]
    [InlineData("abc.AAAA", "AAAA")]
    [InlineData("1000.no es base64!", "AAAA")]
    [InlineData("1000.AAAA", "no es base64!")]
    public void A_malformed_hash_or_salt_never_verifies_and_never_throws(string hash, string salt)
    {
        Assert.False(LocalPasswordHasher.Verify("clave", hash, salt));
    }

    [Fact]
    public void Hashing_an_empty_password_is_refused()
    {
        Assert.Throws<ArgumentException>(() => LocalPasswordHasher.Hash(""));
    }
}

public sealed class DpapiTokenProtectorTests
{
    private readonly DpapiTokenProtector _protector = new();

    [Fact]
    public void A_token_round_trips()
    {
        string token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.firma";

        Assert.Equal(token, _protector.Unprotect(_protector.Protect(token)));
    }

    [Fact]
    public void The_protected_text_does_not_contain_the_token()
    {
        string token = "secreto-muy-largo-del-token";

        string protectedText = _protector.Protect(token);

        Assert.DoesNotContain(token, protectedText);
        Assert.DoesNotContain(token, Encoding.UTF8.GetString(Convert.FromBase64String(protectedText)));
    }

    [Fact]
    public void Protecting_the_same_text_twice_gives_different_blobs_that_both_open()
    {
        string a = _protector.Protect("token");
        string b = _protector.Protect("token");

        Assert.NotEqual(a, b);
        Assert.Equal("token", _protector.Unprotect(a));
        Assert.Equal("token", _protector.Unprotect(b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto no es base64!")]
    [InlineData("AAAAAAAA")]
    public void Text_that_was_not_protected_by_this_user_comes_back_as_null(string garbage)
    {
        Assert.Null(_protector.Unprotect(garbage));
    }

    [Fact]
    public void A_tampered_blob_comes_back_as_null()
    {
        byte[] blob = Convert.FromBase64String(_protector.Protect("token"));
        blob[^1] ^= 0xFF;

        Assert.Null(_protector.Unprotect(Convert.ToBase64String(blob)));
    }

    [Fact]
    public void Non_ascii_tokens_survive()
    {
        Assert.Equal("ñandú-€-🚀", _protector.Unprotect(_protector.Protect("ñandú-€-🚀")));
    }
}

public sealed class FileDeviceIdProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "heartbeat-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void The_id_is_created_once_and_survives_restarts()
    {
        string path = Path.Combine(_dir, "device-id.txt");

        string first = new FileDeviceIdProvider(path).DeviceId;
        string second = new FileDeviceIdProvider(path).DeviceId;

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, second);
        Assert.Equal(first, File.ReadAllText(path));
    }

    [Fact]
    public void An_empty_file_is_replaced_by_a_new_id()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "device-id.txt");
        File.WriteAllText(path, "   ");

        string id = new FileDeviceIdProvider(path).DeviceId;

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(id, File.ReadAllText(path));
    }

    [Fact]
    public void Two_computers_get_different_ids()
    {
        Assert.NotEqual(
            new FileDeviceIdProvider(Path.Combine(_dir, "a", "id")).DeviceId,
            new FileDeviceIdProvider(Path.Combine(_dir, "b", "id")).DeviceId);
    }

    [Fact]
    public void The_id_is_stable_within_one_provider()
    {
        FileDeviceIdProvider provider = new(Path.Combine(_dir, "id"));

        Assert.Equal(provider.DeviceId, provider.DeviceId);
    }
}

public sealed class CrewApiOptionsTests
{
    private static CrewApiOptions From(params (string Key, string? Value)[] values) =>
        CrewApiOptions.From(new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build());

    [Fact]
    public void Without_configuration_the_app_runs_in_demo_mode_with_local_defaults()
    {
        CrewApiOptions options = CrewApiOptions.From(null);

        Assert.True(options.IsDemo);
        Assert.Equal("http://localhost:5027/", options.BaseUrl);
        Assert.Equal(8, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData("Live", false)]
    [InlineData("live", false)]
    [InlineData("LIVE", false)]
    [InlineData("Demo", true)]
    [InlineData("otra cosa", true)]
    public void Only_live_talks_to_the_real_server(string mode, bool isDemo)
    {
        Assert.Equal(isDemo, From(("CrewApi:Mode", mode)).IsDemo);
    }

    [Theory]
    [InlineData("https://api.heartbeat.test", "https://api.heartbeat.test/")]
    [InlineData("https://api.heartbeat.test/", "https://api.heartbeat.test/")]
    public void The_base_url_always_ends_with_a_slash_so_relative_paths_resolve(string configured, string expected)
    {
        Assert.Equal(expected, From(("CrewApi:BaseUrl", configured)).BaseUrl);
    }

    [Theory]
    [InlineData("15", 15)]
    [InlineData("0", 8)]
    [InlineData("-3", 8)]
    [InlineData("abc", 8)]
    public void The_timeout_falls_back_to_a_sane_default(string configured, int expected)
    {
        Assert.Equal(expected, From(("CrewApi:TimeoutSeconds", configured)).TimeoutSeconds);
    }
}
