using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugview.Core.Release;

/// <summary>A newer release with a download for this platform and kind.</summary>
/// <param name="Version">The release's version.</param>
/// <param name="DownloadUrl">Always an https URL on plugview.app.</param>
/// <param name="Sha256">The download's SHA-256, lowercase hex; <c>null</c> when the feed has none (then it can only be downloaded).</param>
/// <param name="Size">The download's size in bytes.</param>
public sealed record Update(string Version, Uri DownloadUrl, string? Sha256, long? Size);

/// <summary>Why the release feed was not used.</summary>
public abstract class FeedException : Exception
{
    private protected FeedException(string message, Exception? innerException = null) : base(message, innerException) { }
}

/// <summary>The feed uses a schema this build cannot read.</summary>
public sealed class UnsupportedSchemaException(long schemaVersion) : FeedException($"unsupported release feed schema {schemaVersion}")
{
    /// <summary>The schema the feed declared.</summary>
    public long SchemaVersion { get; } = schemaVersion;
}

/// <summary>The download is not an https URL on plugview.app.</summary>
public sealed class UntrustedUrlException(string url) : FeedException($"untrusted download URL {url}")
{
    /// <summary>The refused URL, as written in the feed.</summary>
    public string Url { get; } = url;
}

/// <summary>The feed is not JSON, or misses a field this build reads.</summary>
public sealed class MalformedFeedException(Exception innerException) : FeedException("malformed release feed", innerException);

/// <summary>The self-hosted release feed, <c>https://plugview.app/releases/latest.json</c>, one file for every platform.</summary>
public static class ReleaseFeed
{
    /// <summary>Where the feed lives.</summary>
    public static readonly Uri Url = new("https://plugview.app/releases/latest.json");

    private const long SchemaVersion = 1;
    private const string Host = "plugview.app";

    /// <summary>The release to offer: newer than <paramref name="currentVersion"/> with a <paramref name="platform"/>/<paramref name="kind"/> download, else <c>null</c>.</summary>
    /// <exception cref="FeedException">An unknown schema, a download outside https://plugview.app, or a malformed feed.</exception>
    public static Update? UpdateFrom(ReadOnlySpan<byte> feed, string currentVersion, string platform, string kind)
    {
        var manifest = Decode(feed);
        if (manifest.SchemaVersion != SchemaVersion) throw new UnsupportedSchemaException(manifest.SchemaVersion);
        if (!ReleaseVersion.IsNewer(manifest.Version, currentVersion)) return null;

        var artifact = manifest.Artifacts.FirstOrDefault(artifact => artifact.Platform == platform && artifact.Kind == kind);
        if (artifact is null) return null;
        return TrustedUrl(artifact.Url) is { } url
            ? new Update(manifest.Version, url, Checksum(artifact.Sha256), artifact.Size is > 0 ? artifact.Size : null)
            : throw new UntrustedUrlException(artifact.Url);
    }

    private static Manifest Decode(ReadOnlySpan<byte> feed)
    {
        try
        {
            return JsonSerializer.Deserialize(feed, FeedJsonContext.Default.Manifest) ?? throw new JsonException("null feed");
        }
        catch (JsonException error)
        {
            throw new MalformedFeedException(error);
        }
    }

    /// <summary>Schemes and host names are case-insensitive; user info, subdomains and a trailing dot are other hosts.</summary>
    private static Uri? TrustedUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps
        && string.Equals(url.Host, Host, StringComparison.OrdinalIgnoreCase)
            ? url
            : null;

    internal sealed record Manifest(long SchemaVersion, string Version, IReadOnlyList<Artifact> Artifacts);

    internal sealed record Artifact(string Platform, string Kind, string Url, string? Sha256 = null, long? Size = null);

    /// <summary>A SHA-256 as 64 hexadecimal characters, lowercased; anything else is not trusted for installing.</summary>
    private static string? Checksum(string? text) =>
        text is { Length: 64 } && text.All(char.IsAsciiHexDigit) ? text.ToLowerInvariant() : null;
}

/// <summary>Source-generated JSON, so the feed reads without reflection under NativeAOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ReleaseFeed.Manifest))]
internal sealed partial class FeedJsonContext : JsonSerializerContext;
