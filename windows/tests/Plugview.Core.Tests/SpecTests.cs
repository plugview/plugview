using System.Text;
using System.Text.Json.Nodes;
using Plugview.Core.Brand;
using Plugview.Core.CommandLine;
using Plugview.Core.Launcher;
using Plugview.Core.Measurement;
using Plugview.Core.Release;
using Plugview.Core.Selection;

namespace Plugview.Core.Tests;

/// <summary>Runs every shared fixture in <c>spec/fixtures</c> (see <c>spec/README.md</c>) against the Windows port.</summary>
public sealed class SpecTests
{
    [Fact]
    public void DeviceSelectionMatchesTheSpec()
    {
        var spec = Fixture.Load("device-selection.json");
        var devices = new FixtureDevices(spec["devices"]);

        foreach (var testCase in spec["playableVideoDevices"].Items().OfType<JsonNode>())
        {
            var result = DeviceSelection.PlayableVideoDevices(devices.Many(testCase["candidates"]), testCase["query"].OptionalText());
            Assert.True(devices.Many(testCase["expected"]).SequenceEqual(result), testCase.Description());
        }
        foreach (var testCase in spec["selectVideoDevice"].Items().OfType<JsonNode>())
        {
            var result = DeviceSelection.SelectVideoDevice(devices.Many(testCase["candidates"]), testCase["query"].OptionalText());
            Assert.True(devices.Optional(testCase["expected"]) == result, testCase.Description());
        }
        foreach (var testCase in spec["selectAudioDevice"].Items().OfType<JsonNode>())
        {
            var result = DeviceSelection.SelectAudioDevice(devices.Many(testCase["candidates"]), devices.One(testCase["video"]), testCase["query"].OptionalText());
            Assert.True(devices.Optional(testCase["expected"]) == result, testCase.Description());
        }
        foreach (var testCase in spec["preferredFirst"].Items().OfType<JsonNode>())
        {
            var result = DeviceSelection.PreferredFirst(devices.Many(testCase["devices"]), testCase["preferredID"].OptionalText());
            Assert.True(devices.Many(testCase["expected"]).SequenceEqual(result), testCase.Description());
        }
        foreach (var testCase in spec["shouldAutoPlay"].Items().OfType<JsonNode>())
        {
            var result = DeviceSelection.ShouldAutoPlay(devices.Many(testCase["devices"]), testCase["preferredID"].OptionalText());
            Assert.True(testCase["expected"]!.GetValue<bool>() == result, testCase.Description());
        }
    }

    [Fact]
    public void VideoModesMatchTheSpec()
    {
        var spec = Fixture.Load("video-modes.json");
        foreach (var testCase in spec["bestModeIndex"].Items().OfType<JsonNode>())
        {
            Assert.True(OptionalIndex(testCase["expected"]) == VideoModes.BestModeIndex(Fixture.Modes(testCase["modes"])), testCase.Description());
        }
        foreach (var testCase in spec["pickableModes"].Items().OfType<JsonNode>())
        {
            var keys = VideoModes.PickableModes(Fixture.Modes(testCase["modes"])).Select(choice => choice.Key);
            Assert.True(testCase["expected"].Items().Select(key => key.Text()).SequenceEqual(keys), testCase.Description());
        }
        foreach (var testCase in spec["modeIndex"].Items().OfType<JsonNode>())
        {
            var choice = testCase["choice"].OptionalText() is { } key ? ModeChoice.FromKey(key) : null;
            Assert.True(OptionalIndex(testCase["expected"]) == VideoModes.ModeIndex(Fixture.Modes(testCase["modes"]), choice), testCase.Description());
        }
        foreach (var testCase in spec["modeChoiceFromMode"].Items().OfType<JsonNode>())
        {
            var choice = ModeChoice.From(Fixture.Mode(testCase["mode"]));
            Assert.Equal(testCase["key"].Text(), choice.Key);
            Assert.Equal(testCase["title"].Text(), choice.Title);
        }
        foreach (var valid in spec["modeChoiceKeys"]?["valid"].Items().OfType<JsonNode>() ?? [])
        {
            Assert.Equal(new ModeChoice(valid["width"].Integer(), valid["height"].Integer(), valid["frameRate"].Integer()), ModeChoice.FromKey(valid["key"].Text()));
        }
        foreach (var invalid in spec["modeChoiceKeys"]?["invalid"].Items().OfType<JsonNode>() ?? [])
        {
            Assert.True(ModeChoice.FromKey(invalid.Text()) is null, invalid.Text());
        }
        foreach (var testCase in spec["summary"].Items().OfType<JsonNode>())
        {
            Assert.Equal(testCase["expected"].Text(), Fixture.Mode(testCase["mode"]).Summary);
        }
    }

    [Fact]
    public void LauncherMatchesTheSpec()
    {
        var spec = Fixture.Load("launcher.json");
        var devices = new FixtureDevices(spec["devices"]);
        var failure = new CaptureFailure(spec["failure"]?["deviceName"].Text() ?? "", spec["failure"]?["reason"].Text() ?? "");

        LauncherState State(JsonNode? node) => node?["state"].Text() switch
        {
            "permissionNeeded" => new LauncherState.PermissionNeeded(Permission(node)),
            "permissionDenied" => new LauncherState.PermissionDenied(Permission(node)),
            "waitingForSignal" => new LauncherState.WaitingForSignal(),
            "ready" => new LauncherState.Ready(devices.Many(node["devices"])),
            "playing" => new LauncherState.Playing(devices.One(node["device"])),
            "failed" => new LauncherState.Failed(failure),
            var other => throw new InvalidDataException($"unknown state {other}"),
        };

        LauncherEvent Event(JsonNode? node) => node?["event"].Text() switch
        {
            "permissionRequested" => new LauncherEvent.PermissionRequested(Permission(node)),
            "permissionDenied" => new LauncherEvent.PermissionDenied(Permission(node)),
            "permissionsGranted" => new LauncherEvent.PermissionsGranted(),
            "devicesChanged" => new LauncherEvent.DevicesChanged(devices.Many(node["devices"])),
            "playbackStarted" => new LauncherEvent.PlaybackStarted(devices.One(node["device"])),
            "playbackFailed" => new LauncherEvent.PlaybackFailed(failure),
            "playbackStopped" => new LauncherEvent.PlaybackStopped(),
            var other => throw new InvalidDataException($"unknown event {other}"),
        };

        foreach (var testCase in spec["transitions"].Items().OfType<JsonNode>())
        {
            Assert.True(State(testCase["expected"]) == LauncherTransitions.Next(State(testCase["from"]), Event(testCase["event"])), testCase.Description());
        }
        foreach (var sequence in spec["sequences"].Items().OfType<JsonNode>())
        {
            var state = State(sequence["from"]);
            var stepNumber = 0;
            foreach (var step in sequence["steps"].Items().OfType<JsonNode>())
            {
                stepNumber++;
                state = LauncherTransitions.Next(state, Event(step["event"]));
                Assert.True(State(step["expected"]) == state, $"{sequence.Description()}, step {stepNumber}");
            }
        }
    }

    private static PermissionKind Permission(JsonNode node) => node["permission"].Text() switch
    {
        "camera" => PermissionKind.Camera,
        "microphone" => PermissionKind.Microphone,
        var other => throw new InvalidDataException($"unknown permission {other}"),
    };

    [Fact]
    public void CommandLineMatchesTheSpec()
    {
        var spec = Fixture.Load("command-line.json");
        var defaults = spec["defaults"] ?? throw new InvalidDataException("defaults");
        Assert.Equal(Options(defaults, null), new RunOptions());

        foreach (var testCase in spec["cases"].Items().OfType<JsonNode>())
        {
            var arguments = testCase["arguments"].Items().Select(argument => argument.Text()).ToList();
            var expected = testCase["expected"] ?? throw new InvalidDataException("expected");
            if (expected["error"].OptionalText() is { } message)
            {
                var error = Assert.Throws<OptionsException>(() => CommandParser.Parse(arguments));
                Assert.True(message == error.Message, $"{testCase.Description()}: got '{error.Message}'");
                continue;
            }
            Command expectedCommand = expected["command"].Text() switch
            {
                "run" => new Command.Run(Options(defaults, expected["options"])),
                "measure" => new Command.Measure(Options(defaults, expected["options"])),
                "listDevices" => new Command.ListDevices(),
                "showHelp" => new Command.ShowHelp(),
                "showVersion" => new Command.ShowVersion(),
                var other => throw new InvalidDataException($"unknown command {other}"),
            };
            Assert.True(expectedCommand == CommandParser.Parse(arguments), testCase.Description());
        }
    }

    private static RunOptions Options(JsonNode defaults, JsonNode? overrides)
    {
        JsonNode? Field(string name) => overrides is JsonObject fields && fields.ContainsKey(name) ? fields[name] : defaults[name];
        return new RunOptions
        {
            VideoQuery = Field("videoQuery").OptionalText(),
            AudioQuery = Field("audioQuery").OptionalText(),
            IsAudioEnabled = Field("isAudioEnabled")!.GetValue<bool>(),
            VolumePercent = Field("volumePercent").Integer(),
            StartsInFullScreen = Field("startsInFullScreen")!.GetValue<bool>(),
            Mode = Field("mode").OptionalText() is { } key ? ModeChoice.FromKey(key) : null,
        };
    }

    [Fact]
    public void ReleaseFeedMatchesTheSpec()
    {
        foreach (var testCase in Fixture.Load("release-feed.json")["cases"].Items().OfType<JsonNode>())
        {
            var feed = testCase["feedText"].OptionalText() is { } text ? Encoding.UTF8.GetBytes(text) : Encoding.UTF8.GetBytes(testCase["feed"]!.ToJsonString());
            var expected = testCase["expected"] ?? throw new InvalidDataException("expected");
            var description = testCase.Description();
            try
            {
                var update = ReleaseFeed.UpdateFrom(feed, testCase["currentVersion"].Text(), testCase["platform"].Text(), testCase["kind"].Text());
                Assert.True(expected["error"] is null, $"{description}: read, expected an error");
                var expectedUpdate = expected["update"];
                Assert.True(expectedUpdate?["version"].OptionalText() == update?.Version, description);
                Assert.True(expectedUpdate?["url"].OptionalText() == update?.DownloadUrl.OriginalString, description);
            }
            catch (UnsupportedSchemaException error)
            {
                Assert.True(expected["error"].OptionalText() == "unsupportedSchema" && expected["schemaVersion"].Integer() == error.SchemaVersion, description);
            }
            catch (UntrustedUrlException error)
            {
                Assert.True(expected["error"].OptionalText() == "untrustedURL" && expected["url"].Text() == error.Url, description);
            }
            catch (MalformedFeedException)
            {
                Assert.True(expected["error"].OptionalText() == "malformed", description);
            }
        }
    }

    [Fact]
    public void VersionsMatchTheSpec()
    {
        var spec = Fixture.Load("versions.json");
        foreach (var pair in spec["newer"].Items().OfType<JsonNode>())
        {
            Assert.True(ReleaseVersion.IsNewer(pair["candidate"].Text(), pair["current"].Text()), pair.ToJsonString());
        }
        foreach (var pair in spec["notNewer"].Items().OfType<JsonNode>())
        {
            Assert.False(ReleaseVersion.IsNewer(pair["candidate"].Text(), pair["current"].Text()), pair.ToJsonString());
        }
    }

    [Fact]
    public void LatencyIssuesMatchTheSpec()
    {
        var spec = Fixture.Load("latency-advice.json");
        foreach (var testCase in spec["cases"].Items().OfType<JsonNode>())
        {
            var facts = spec["healthy"]!.AsObject().DeepClone().AsObject();
            foreach (var (name, value) in testCase["facts"]!.AsObject()) facts[name] = value?.DeepClone();
            var latencyFacts = new LatencyFacts(
                facts["requestedFrameRate"].Number(),
                facts["deliveredFrameRate"].Number(),
                facts["displayRefreshRate"]?.GetValue<double>(),
                facts["outputLatencyMilliseconds"]?.GetValue<double>(),
                facts["isOutputBluetooth"]!.GetValue<bool>());
            var expected = testCase["expected"].Items().OfType<JsonNode>().Select(issue => (LatencyIssue)(issue["issue"].Text() switch
            {
                "lowFrameRate" => new LatencyIssue.LowFrameRate(issue["delivered"].Integer(), issue["requested"].Integer()),
                "bluetoothOutput" => new LatencyIssue.BluetoothOutput(),
                "slowOutput" => new LatencyIssue.SlowOutput(issue["milliseconds"].Integer()),
                "unevenRefresh" => new LatencyIssue.UnevenRefresh(issue["refreshRate"].Integer(), issue["frameRate"].Integer()),
                var other => throw new InvalidDataException($"unknown issue {other}"),
            }));
            Assert.True(expected.SequenceEqual(LatencyAdvice.Issues(latencyFacts)), testCase.Description());
        }
    }

    [Fact]
    public void PercentilesMatchTheSpec()
    {
        foreach (var testCase in Fixture.Load("percentile.json").Items().OfType<JsonNode>())
        {
            var samples = testCase["samples"].Items().Select(sample => sample.Number());
            Assert.True(testCase["expected"]?.GetValue<double>() == LatencyAdvice.Percentile(samples, testCase["fraction"].Number()), testCase.Description());
        }
    }

    [Fact]
    public void TuningMatchesTheSpec()
    {
        var spec = Fixture.Load("tuning.json");
        Assert.Equal(Tuning.SmoothFrameRate, spec["smoothFrameRate"].Number());
        Assert.Equal(Tuning.CardAudioWaitSeconds, spec["cardAudioWaitSeconds"].Number());
        Assert.Equal(Tuning.VolumeStepPercent, spec["volumeStepPercent"].Integer());
        Assert.Equal(Tuning.ControlsHideSeconds, spec["controlsHideSeconds"].Number());
        Assert.Equal(Tuning.UpdateCheckIntervalSeconds, spec["updateCheckIntervalSeconds"].Number());
        Assert.Equal(Tuning.MeasurementSeconds, spec["measurementSeconds"].Number());
        Assert.Equal(Tuning.SlowOutputLatencyMilliseconds, spec["slowOutputLatencyMilliseconds"].Number());
        Assert.Equal(Tuning.NoSignalAfterSeconds, spec["noSignalAfterSeconds"].Number());
        Assert.Equal(spec["reopenBackoffSeconds"].Items().Select(seconds => seconds.Number()), Tuning.ReopenBackoffSeconds);
        Assert.Equal(Tuning.SignalHelpAfterAttempts, spec["signalHelpAfterAttempts"].Integer());
        Assert.Equal(Tuning.BlackLumaMax, spec["blackLumaMax"].Integer());
        Assert.Equal(Tuning.BlackPictureSeconds, spec["blackPictureSeconds"].Number());
        Assert.Equal(Tuning.SilentAudioSeconds, spec["silentAudioSeconds"].Number());
    }

    [Fact]
    public void SignalWatchMatchesTheSpec()
    {
        var spec = Fixture.Load("signal-watch.json");
        foreach (var testCase in spec["watch"].Items().OfType<JsonNode>())
        {
            var expected = testCase["expected"];
            var expectedWatch = new SignalWatch(
                expected!["showsNoSignal"]!.GetValue<bool>(), expected["shouldReopen"]!.GetValue<bool>(), expected["showsSignalHelp"]!.GetValue<bool>());
            var watch = SignalWatcher.Watch(testCase["secondsSinceLastFrame"].Number(), (int)testCase["reopenAttempts"].Integer());
            Assert.True(expectedWatch == watch, testCase.Description());
        }
        foreach (var testCase in spec["blackFrame"].Items().OfType<JsonNode>())
        {
            Assert.True(testCase["expected"]!.GetValue<bool>() == SignalWatcher.IsBlackFrame((int)testCase["maxLuma"].Integer()), testCase.ToJsonString());
        }
        foreach (var testCase in spec["hints"].Items().OfType<JsonNode>())
        {
            var expected = testCase["expected"].Items().Select(hint => hint.Text() switch
            {
                "blackPicture" => SignalHint.BlackPicture,
                "noGameSound" => SignalHint.NoGameSound,
                var other => throw new InvalidDataException($"unknown hint {other}"),
            });
            var hints = SignalWatcher.Hints(testCase["blackPictureSeconds"].Number(), testCase["silentAudioSeconds"]?.GetValue<double>());
            Assert.True(expected.SequenceEqual(hints), testCase.Description());
        }
    }

    [Fact]
    public void CaptureToolsMatchTheSpec()
    {
        var spec = Fixture.Load("capture-tools.json");
        foreach (var testCase in spec["screenshotFileName"].Items().OfType<JsonNode>())
        {
            var date = testCase["date"];
            int Part(string name) => (int)date?[name].Integer()!;
            Assert.Equal(testCase["expected"].Text(), Screenshots.FileName(Part("year"), Part("month"), Part("day"), Part("hour"), Part("minute"), Part("second")));
        }
        var expansion = spec["limitedRangeExpansion"];
        foreach (var sample in expansion?["samples"].Items().OfType<JsonNode>() ?? [])
        {
            Assert.True(Math.Abs(LimitedRangeExpansion.Expand(sample["input"].Number()) - sample["expected"].Number()) < expansion!["tolerance"].Number(), sample.ToJsonString());
        }
    }

    [Fact]
    public void EasingMatchesTheSpec()
    {
        var spec = Fixture.Load("easing.json");
        var tolerance = spec["tolerance"].Number();
        foreach (var curve in spec["cubicBezier"].Items().OfType<JsonNode>())
        {
            var easing = Bezier(curve["points"]);
            foreach (var sample in curve["samples"].Items().OfType<JsonNode>())
            {
                var (x, expected) = (sample[0].Number(), sample[1].Number());
                Assert.True(Math.Abs(easing.Evaluate(x) - expected) < tolerance, $"{curve["name"].Text()} at {x}");
            }
        }
        foreach (var testCase in spec["keyframes"].Items().OfType<JsonNode>())
        {
            var keyframes = new Keyframes([.. testCase["frames"].Items().Select(frame => (frame![0].Number(), frame[1].Number()))], Bezier(testCase["easing"]));
            foreach (var sample in testCase["samples"].Items().OfType<JsonNode>())
            {
                var (progress, expected) = (sample[0].Number(), sample[1].Number());
                Assert.True(Math.Abs(keyframes.ValueAt(progress) - expected) < tolerance, $"{testCase.Description()} at {progress}");
            }
        }
        Assert.Equal(new CubicBezier(0.25, 0.1, 0.25, 1), CubicBezier.Ease);
        Assert.Equal(new CubicBezier(0.42, 0, 0.58, 1), CubicBezier.EaseInOut);
    }

    private static CubicBezier Bezier(JsonNode? points)
    {
        var values = points.Items().Select(point => point.Number()).ToArray();
        return values is [var x1, var y1, var x2, var y2] ? new CubicBezier(x1, y1, x2, y2) : throw new InvalidDataException("a curve needs four numbers");
    }

    /// <summary>One version on every platform: the macOS constant is the source.</summary>
    [Fact]
    public void VersionMatchesTheMac()
    {
        var swift = File.ReadAllLines(Path.Combine(Fixture.RepositoryRoot(), "Sources", "PlugviewCore", "Constants.swift"));
        Assert.Contains($"public let plugviewVersion = \"{Tuning.PlugviewVersion}\"", swift);
    }

    /// <summary>The Windows wording of every issue; the Mac's says "Mac speakers".</summary>
    [Fact]
    public void AdviceIsWordedForWindows()
    {
        var advice = LatencyAdvice.Advice(new LatencyFacts(60, 60, 120, 55, IsOutputBluetooth: false));
        Assert.Equal(["The playback device adds 55 ms. Wired headphones or the PC speakers are faster."], advice);
    }

    private static int? OptionalIndex(JsonNode? node) => node is null ? null : (int)node.Integer();
}
