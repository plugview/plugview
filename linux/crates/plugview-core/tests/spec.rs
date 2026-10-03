//! Runs the shared behavior fixtures in `spec/fixtures` (see `spec/README.md`) against this port.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic, clippy::float_cmp)]

use std::path::PathBuf;

use plugview_core::*;
use serde_json::{Map, Value};

fn fixture(name: &str) -> Value {
    let path: PathBuf = [env!("CARGO_MANIFEST_DIR"), "..", "..", "..", "spec", "fixtures", name].iter().collect();
    let text = std::fs::read_to_string(&path).unwrap_or_else(|error| panic!("{}: {error}", path.display()));
    serde_json::from_str(&text).unwrap()
}

fn text(value: &Value) -> &str {
    value.as_str().unwrap_or_else(|| panic!("expected a string, got {value}"))
}

fn optional_text(value: &Value) -> Option<&str> {
    value.as_str()
}

fn array(value: &Value) -> &Vec<Value> {
    value.as_array().unwrap_or_else(|| panic!("expected an array, got {value}"))
}

fn number(value: &Value) -> f64 {
    value.as_f64().unwrap_or_else(|| panic!("expected a number, got {value}"))
}

fn integer(value: &Value) -> i64 {
    value.as_i64().unwrap_or_else(|| panic!("expected an integer, got {value}"))
}

fn description(case: &Value) -> &str {
    case["description"].as_str().unwrap_or("(no description)")
}

// --- Devices ---

fn device_from(value: &Value) -> DeviceInfo {
    let transport = match text(&value["transport"]) {
        "builtIn" => DeviceTransport::BuiltIn,
        "usb" => DeviceTransport::Usb,
        "virtual" => DeviceTransport::Virtual,
        "wireless" => DeviceTransport::Wireless,
        "other" => DeviceTransport::Other,
        other => panic!("unknown transport {other}"),
    };
    DeviceInfo {
        unique_id: text(&value["uniqueID"]).to_owned(),
        name: text(&value["name"]).to_owned(),
        model_id: text(&value["modelID"]).to_owned(),
        transport,
    }
}

struct Devices<'a>(&'a Map<String, Value>);

impl Devices<'_> {
    fn one(&self, key: &Value) -> DeviceInfo {
        device_from(self.0.get(text(key)).unwrap_or_else(|| panic!("unknown device {key}")))
    }

    fn optional(&self, key: &Value) -> Option<DeviceInfo> {
        (!key.is_null()).then(|| self.one(key))
    }

    fn many(&self, keys: &Value) -> Vec<DeviceInfo> {
        array(keys).iter().map(|key| self.one(key)).collect()
    }
}

#[test]
fn device_selection_matches_the_spec() {
    let spec = fixture("device-selection.json");
    let devices = Devices(spec["devices"].as_object().unwrap());

    for case in array(&spec["playableVideoDevices"]) {
        let result = playable_video_devices(&devices.many(&case["candidates"]), optional_text(&case["query"]));
        assert_eq!(result, devices.many(&case["expected"]), "{}", description(case));
    }
    for case in array(&spec["selectVideoDevice"]) {
        let result = select_video_device(&devices.many(&case["candidates"]), optional_text(&case["query"]));
        assert_eq!(result, devices.optional(&case["expected"]), "{}", description(case));
    }
    for case in array(&spec["selectAudioDevice"]) {
        let result = select_audio_device(&devices.many(&case["candidates"]), &devices.one(&case["video"]), optional_text(&case["query"]));
        assert_eq!(result, devices.optional(&case["expected"]), "{}", description(case));
    }
    for case in array(&spec["preferredFirst"]) {
        let result = preferred_first(&devices.many(&case["devices"]), optional_text(&case["preferredID"]));
        assert_eq!(result, devices.many(&case["expected"]), "{}", description(case));
    }
    for case in array(&spec["shouldAutoPlay"]) {
        let result = should_auto_play(&devices.many(&case["devices"]), optional_text(&case["preferredID"]));
        assert_eq!(Some(result), case["expected"].as_bool(), "{}", description(case));
    }
}

// --- Video modes ---

fn mode_from(value: &Value) -> VideoMode {
    let encoding = match text(&value["encoding"]) {
        "compressed" => FrameEncoding::Compressed,
        "converted" => FrameEncoding::Converted,
        "native" => FrameEncoding::Native,
        other => panic!("unknown encoding {other}"),
    };
    VideoMode { width: integer(&value["width"]), height: integer(&value["height"]), max_frame_rate: number(&value["maxFrameRate"]), encoding }
}

fn modes_from(value: &Value) -> Vec<VideoMode> {
    array(value).iter().map(mode_from).collect()
}

fn optional_index(value: &Value) -> Option<usize> {
    value.as_u64().map(|index| index as usize)
}

#[test]
fn video_modes_match_the_spec() {
    let spec = fixture("video-modes.json");
    for case in array(&spec["bestModeIndex"]) {
        assert_eq!(best_mode_index(&modes_from(&case["modes"])), optional_index(&case["expected"]), "{}", description(case));
    }
    for case in array(&spec["pickableModes"]) {
        let keys: Vec<String> = pickable_modes(&modes_from(&case["modes"])).iter().map(ModeChoice::key).collect();
        let expected: Vec<&str> = array(&case["expected"]).iter().map(text).collect();
        assert_eq!(keys, expected, "{}", description(case));
    }
    for case in array(&spec["modeIndex"]) {
        let choice = optional_text(&case["choice"]).map(|key| ModeChoice::from_key(key).unwrap());
        assert_eq!(mode_index(&modes_from(&case["modes"]), choice), optional_index(&case["expected"]), "{}", description(case));
    }
    for case in array(&spec["modeChoiceFromMode"]) {
        let choice = ModeChoice::from_mode(&mode_from(&case["mode"]));
        assert_eq!(choice.key(), text(&case["key"]), "{}", description(case));
        assert_eq!(choice.title(), text(&case["title"]), "{}", description(case));
    }
    for valid in array(&spec["modeChoiceKeys"]["valid"]) {
        let expected = ModeChoice { width: integer(&valid["width"]), height: integer(&valid["height"]), frame_rate: integer(&valid["frameRate"]) };
        assert_eq!(ModeChoice::from_key(text(&valid["key"])), Some(expected), "{}", valid["key"]);
    }
    for invalid in array(&spec["modeChoiceKeys"]["invalid"]) {
        assert_eq!(ModeChoice::from_key(text(invalid)), None, "{invalid}");
    }
    for case in array(&spec["summary"]) {
        assert_eq!(mode_from(&case["mode"]).summary(), text(&case["expected"]), "{}", description(case));
    }
}

// --- Launcher ---

fn permission_from(value: &Value) -> PermissionKind {
    match text(&value["permission"]) {
        "camera" => PermissionKind::Camera,
        "microphone" => PermissionKind::Microphone,
        other => panic!("unknown permission {other}"),
    }
}

struct LauncherSpec<'a> {
    devices: Devices<'a>,
    failure: CaptureFailure,
}

impl LauncherSpec<'_> {
    fn state(&self, value: &Value) -> LauncherState {
        match text(&value["state"]) {
            "permissionNeeded" => LauncherState::PermissionNeeded(permission_from(value)),
            "permissionDenied" => LauncherState::PermissionDenied(permission_from(value)),
            "waitingForSignal" => LauncherState::WaitingForSignal,
            "ready" => LauncherState::Ready(self.devices.many(&value["devices"])),
            "playing" => LauncherState::Playing(self.devices.one(&value["device"])),
            "failed" => LauncherState::Failed(self.failure.clone()),
            other => panic!("unknown state {other}"),
        }
    }

    fn event(&self, value: &Value) -> LauncherEvent {
        match text(&value["event"]) {
            "permissionRequested" => LauncherEvent::PermissionRequested(permission_from(value)),
            "permissionDenied" => LauncherEvent::PermissionDenied(permission_from(value)),
            "permissionsGranted" => LauncherEvent::PermissionsGranted,
            "devicesChanged" => LauncherEvent::DevicesChanged(self.devices.many(&value["devices"])),
            "playbackStarted" => LauncherEvent::PlaybackStarted(self.devices.one(&value["device"])),
            "playbackFailed" => LauncherEvent::PlaybackFailed(self.failure.clone()),
            "playbackStopped" => LauncherEvent::PlaybackStopped,
            other => panic!("unknown event {other}"),
        }
    }
}

#[test]
fn launcher_matches_the_spec() {
    let spec = fixture("launcher.json");
    let launcher = LauncherSpec {
        devices: Devices(spec["devices"].as_object().unwrap()),
        failure: CaptureFailure { device_name: text(&spec["failure"]["deviceName"]).to_owned(), reason: text(&spec["failure"]["reason"]).to_owned() },
    };
    for case in array(&spec["transitions"]) {
        let next = next_launcher_state(&launcher.state(&case["from"]), launcher.event(&case["event"]));
        assert_eq!(next, launcher.state(&case["expected"]), "{}", description(case));
    }
    for sequence in array(&spec["sequences"]) {
        let mut state = launcher.state(&sequence["from"]);
        for (index, step) in array(&sequence["steps"]).iter().enumerate() {
            state = next_launcher_state(&state, launcher.event(&step["event"]));
            assert_eq!(state, launcher.state(&step["expected"]), "{}, step {}", description(sequence), index + 1);
        }
    }
}

// --- Command line ---

fn run_options(defaults: &Value, overrides: &Value) -> RunOptions {
    let field = |name: &str| overrides.get(name).unwrap_or(&defaults[name]);
    RunOptions {
        video_query: optional_text(field("videoQuery")).map(str::to_owned),
        audio_query: optional_text(field("audioQuery")).map(str::to_owned),
        is_audio_enabled: field("isAudioEnabled").as_bool().unwrap(),
        volume_percent: integer(field("volumePercent")),
        starts_in_full_screen: field("startsInFullScreen").as_bool().unwrap(),
        mode: optional_text(field("mode")).map(|key| ModeChoice::from_key(key).unwrap()),
    }
}

#[test]
fn command_line_matches_the_spec() {
    let spec = fixture("command-line.json");
    let defaults = &spec["defaults"];
    assert_eq!(RunOptions::default(), run_options(defaults, &Value::Null), "defaults");

    for case in array(&spec["cases"]) {
        let arguments: Vec<&str> = array(&case["arguments"]).iter().map(text).collect();
        let expected = &case["expected"];
        let expected = match expected.get("command").map(text) {
            None => Err(OptionsError(text(&expected["error"]).to_owned())),
            Some("run") => Ok(Command::Run(run_options(defaults, &expected["options"]))),
            Some("measure") => Ok(Command::Measure(run_options(defaults, &expected["options"]))),
            Some("listDevices") => Ok(Command::ListDevices),
            Some("showHelp") => Ok(Command::ShowHelp),
            Some("showVersion") => Ok(Command::ShowVersion),
            Some(other) => panic!("unknown command {other}"),
        };
        assert_eq!(parse_command(&arguments), expected, "{}", description(case));
    }
}

// --- Release feed and versions ---

#[test]
fn release_feed_matches_the_spec() {
    let spec = fixture("release-feed.json");
    for case in array(&spec["cases"]) {
        let feed = match case.get("feedText") {
            Some(feed_text) => text(feed_text).as_bytes().to_vec(),
            None => serde_json::to_vec(&case["feed"]).unwrap(),
        };
        let result = update_from_feed(&feed, text(&case["currentVersion"]), text(&case["platform"]), text(&case["kind"]));
        let expected = &case["expected"];
        match (result, expected.get("error").map(text)) {
            (Ok(update), None) => {
                let expected_update = expected["update"]
                    .as_object()
                    .map(|update| Update { version: text(&update["version"]).to_owned(), download_url: text(&update["url"]).to_owned() });
                assert_eq!(update, expected_update, "{}", description(case));
            }
            (Err(FeedError::UnsupportedSchema(schema)), Some("unsupportedSchema")) => {
                assert_eq!(schema, integer(&expected["schemaVersion"]), "{}", description(case));
            }
            (Err(FeedError::UntrustedUrl(url)), Some("untrustedURL")) => assert_eq!(url, text(&expected["url"]), "{}", description(case)),
            (Err(FeedError::Malformed(_)), Some("malformed")) => {}
            (result, expected) => panic!("{}: got {result:?}, expected {expected:?}", description(case)),
        }
    }
}

#[test]
fn versions_match_the_spec() {
    let spec = fixture("versions.json");
    for pair in array(&spec["newer"]) {
        assert!(is_version_newer(text(&pair["candidate"]), text(&pair["current"])), "{pair}");
    }
    for pair in array(&spec["notNewer"]) {
        assert!(!is_version_newer(text(&pair["candidate"]), text(&pair["current"])), "{pair}");
    }
}

// --- Measurement ---

fn optional_number(value: &Value) -> Option<f64> {
    value.as_f64()
}

#[test]
fn latency_issues_match_the_spec() {
    let spec = fixture("latency-advice.json");
    let healthy = spec["healthy"].as_object().unwrap();
    for case in array(&spec["cases"]) {
        let mut facts = healthy.clone();
        facts.extend(case["facts"].as_object().unwrap().clone());
        let facts = LatencyFacts {
            requested_frame_rate: number(&facts["requestedFrameRate"]),
            delivered_frame_rate: number(&facts["deliveredFrameRate"]),
            display_refresh_rate: optional_number(&facts["displayRefreshRate"]),
            output_latency_milliseconds: optional_number(&facts["outputLatencyMilliseconds"]),
            is_output_bluetooth: facts["isOutputBluetooth"].as_bool().unwrap(),
        };
        let expected: Vec<LatencyIssue> = array(&case["expected"])
            .iter()
            .map(|issue| match text(&issue["issue"]) {
                "lowFrameRate" => LatencyIssue::LowFrameRate { delivered: integer(&issue["delivered"]), requested: integer(&issue["requested"]) },
                "bluetoothOutput" => LatencyIssue::BluetoothOutput,
                "slowOutput" => LatencyIssue::SlowOutput { milliseconds: integer(&issue["milliseconds"]) },
                "unevenRefresh" => LatencyIssue::UnevenRefresh { refresh_rate: integer(&issue["refreshRate"]), frame_rate: integer(&issue["frameRate"]) },
                other => panic!("unknown issue {other}"),
            })
            .collect();
        assert_eq!(latency_issues(&facts), expected, "{}", description(case));
    }
}

#[test]
fn percentiles_match_the_spec() {
    for case in array(&fixture("percentile.json")) {
        let samples: Vec<f64> = array(&case["samples"]).iter().map(number).collect();
        assert_eq!(percentile(&samples, number(&case["fraction"])), optional_number(&case["expected"]), "{}", description(case));
    }
}

// --- Tuning, capture tools, easing, version ---

#[test]
fn tuning_matches_the_spec() {
    let spec = fixture("tuning.json");
    assert_eq!(Tuning::SMOOTH_FRAME_RATE, number(&spec["smoothFrameRate"]));
    assert_eq!(Tuning::CARD_AUDIO_WAIT_SECONDS, number(&spec["cardAudioWaitSeconds"]));
    assert_eq!(Tuning::VOLUME_STEP_PERCENT, integer(&spec["volumeStepPercent"]));
    assert_eq!(Tuning::CONTROLS_HIDE_SECONDS, number(&spec["controlsHideSeconds"]));
    assert_eq!(Tuning::UPDATE_CHECK_INTERVAL_SECONDS, number(&spec["updateCheckIntervalSeconds"]));
    assert_eq!(Tuning::MEASUREMENT_SECONDS, number(&spec["measurementSeconds"]));
    assert_eq!(Tuning::SLOW_OUTPUT_LATENCY_MILLISECONDS, number(&spec["slowOutputLatencyMilliseconds"]));
    assert_eq!(Tuning::NO_SIGNAL_AFTER_SECONDS, number(&spec["noSignalAfterSeconds"]));
    assert_eq!(Tuning::REOPEN_BACKOFF_SECONDS.to_vec(), array(&spec["reopenBackoffSeconds"]).iter().map(number).collect::<Vec<_>>());
    assert_eq!(i64::from(Tuning::SIGNAL_HELP_AFTER_ATTEMPTS), integer(&spec["signalHelpAfterAttempts"]));
    assert_eq!(Tuning::BLACK_LUMA_MAX, integer(&spec["blackLumaMax"]));
    assert_eq!(Tuning::BLACK_PICTURE_SECONDS, number(&spec["blackPictureSeconds"]));
    assert_eq!(Tuning::SILENT_AUDIO_SECONDS, number(&spec["silentAudioSeconds"]));
}

#[test]
fn signal_watch_matches_the_spec() {
    let spec = fixture("signal-watch.json");
    for case in array(&spec["watch"]) {
        let expected = &case["expected"];
        let expected = SignalWatch {
            shows_no_signal: expected["showsNoSignal"].as_bool().unwrap(),
            should_reopen: expected["shouldReopen"].as_bool().unwrap(),
            shows_signal_help: expected["showsSignalHelp"].as_bool().unwrap(),
        };
        let attempts = u32::try_from(integer(&case["reopenAttempts"])).unwrap();
        assert_eq!(signal_watch(number(&case["secondsSinceLastFrame"]), attempts), expected, "{}", description(case));
    }
    for case in array(&spec["blackFrame"]) {
        let max_luma = u8::try_from(integer(&case["maxLuma"])).unwrap();
        assert_eq!(Some(is_black_frame(max_luma)), case["expected"].as_bool(), "{case}");
    }
    for case in array(&spec["hints"]) {
        let expected: Vec<SignalHint> = array(&case["expected"])
            .iter()
            .map(|hint| match text(hint) {
                "blackPicture" => SignalHint::BlackPicture,
                "noGameSound" => SignalHint::NoGameSound,
                other => panic!("unknown hint {other}"),
            })
            .collect();
        let hints = signal_hints(number(&case["blackPictureSeconds"]), optional_number(&case["silentAudioSeconds"]));
        assert_eq!(hints, expected, "{}", description(case));
    }
}

#[test]
fn capture_tools_match_the_spec() {
    let spec = fixture("capture-tools.json");
    for case in array(&spec["screenshotFileName"]) {
        let date = &case["date"];
        let part = |name: &str| integer(&date[name]);
        let name = screenshot_file_name(
            part("year") as i32,
            part("month") as u32,
            part("day") as u32,
            part("hour") as u32,
            part("minute") as u32,
            part("second") as u32,
        );
        assert_eq!(name, text(&case["expected"]));
    }
    let expansion = &spec["limitedRangeExpansion"];
    for sample in array(&expansion["samples"]) {
        let difference = (LimitedRangeExpansion::expand(number(&sample["input"])) - number(&sample["expected"])).abs();
        assert!(difference < number(&expansion["tolerance"]), "{sample}");
    }
}

fn bezier(points: &Value) -> CubicBezier {
    let points: Vec<f64> = array(points).iter().map(number).collect();
    let [x1, y1, x2, y2] = points[..] else { panic!("a curve needs four numbers, got {points:?}") };
    CubicBezier { x1, y1, x2, y2 }
}

#[test]
fn easing_matches_the_spec() {
    let spec = fixture("easing.json");
    let tolerance = number(&spec["tolerance"]);
    for curve in array(&spec["cubicBezier"]) {
        let easing = bezier(&curve["points"]);
        for sample in array(&curve["samples"]) {
            let (x, expected) = (number(&sample[0]), number(&sample[1]));
            assert!((easing.value(x) - expected).abs() < tolerance, "{} at {x}", curve["name"]);
        }
    }
    for case in array(&spec["keyframes"]) {
        let keyframes =
            Keyframes { frames: array(&case["frames"]).iter().map(|frame| (number(&frame[0]), number(&frame[1]))).collect(), easing: bezier(&case["easing"]) };
        for sample in array(&case["samples"]) {
            let (progress, expected) = (number(&sample[0]), number(&sample[1]));
            assert!((keyframes.value(progress) - expected).abs() < tolerance, "{} at {progress}", description(case));
        }
    }
    assert_eq!(CubicBezier::EASE, bezier(&serde_json::json!([0.25, 0.1, 0.25, 1])));
    assert_eq!(CubicBezier::EASE_IN_OUT, bezier(&serde_json::json!([0.42, 0, 0.58, 1])));
}

/// One version on every platform: the macOS constant is the source.
#[test]
fn version_matches_the_mac() {
    let path: PathBuf = [env!("CARGO_MANIFEST_DIR"), "..", "..", "..", "Sources", "PlugviewCore", "Constants.swift"].iter().collect();
    let swift = std::fs::read_to_string(path).unwrap();
    let line = swift.lines().find(|line| line.starts_with("public let plugviewVersion = ")).unwrap();
    assert_eq!(line, format!("public let plugviewVersion = \"{PLUGVIEW_VERSION}\""));
}
