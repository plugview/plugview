using System.Text.Json.Nodes;
using Plugview.Core.Selection;

namespace Plugview.Core.Tests;

/// <summary>Reads the shared fixtures in <c>spec/fixtures</c>: the behavior every platform's port must match.</summary>
internal static class Fixture
{
    public static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    public static JsonNode Load(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "spec", "fixtures", name))) ?? throw new InvalidDataException(name);

    public static string Text(this JsonNode? node) => node?.GetValue<string>() ?? throw new InvalidDataException("expected a string");

    public static string? OptionalText(this JsonNode? node) => node?.GetValue<string>();

    public static double Number(this JsonNode? node) => node?.GetValue<double>() ?? throw new InvalidDataException("expected a number");

    public static long Integer(this JsonNode? node) => node?.GetValue<long>() ?? throw new InvalidDataException("expected an integer");

    public static JsonArray Items(this JsonNode? node) => node?.AsArray() ?? throw new InvalidDataException("expected an array");

    public static string Description(this JsonNode node) => node["description"].OptionalText() ?? "(no description)";

    public static DeviceInfo Device(JsonNode node) => new(
        node["uniqueID"].Text(),
        node["name"].Text(),
        node["modelID"].Text(),
        node["transport"].Text() switch
        {
            "builtIn" => DeviceTransport.BuiltIn,
            "usb" => DeviceTransport.Usb,
            "virtual" => DeviceTransport.Virtual,
            "wireless" => DeviceTransport.Wireless,
            "other" => DeviceTransport.Other,
            var other => throw new InvalidDataException($"unknown transport {other}"),
        });

    public static VideoMode Mode(JsonNode? node) => node is null ? throw new InvalidDataException("expected a mode") : new(
        node["width"].Integer(),
        node["height"].Integer(),
        node["maxFrameRate"].Number(),
        node["encoding"].Text() switch
        {
            "compressed" => FrameEncoding.Compressed,
            "converted" => FrameEncoding.Converted,
            "native" => FrameEncoding.Native,
            var other => throw new InvalidDataException($"unknown encoding {other}"),
        });

    public static List<VideoMode> Modes(JsonNode? node) => [.. node.Items().Select(Mode)];
}

/// <summary>Fixture devices by key, so cases can name them.</summary>
internal sealed class FixtureDevices(JsonNode? devices)
{
    private readonly JsonObject devices = devices?.AsObject() ?? throw new InvalidDataException("expected devices");

    public DeviceInfo One(JsonNode? key) => Fixture.Device(devices[key.Text()] ?? throw new InvalidDataException($"unknown device {key}"));

    public DeviceInfo? Optional(JsonNode? key) => key is null ? null : One(key);

    public List<DeviceInfo> Many(JsonNode? keys) => [.. keys.Items().Select(One)];
}
