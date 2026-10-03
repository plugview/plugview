import PlugviewCore
import Testing

@Suite struct LauncherSpecTests {
    struct Spec: Decodable {
        let devices: FixtureDevices
        let failure: Failure
        let transitions: [Transition]
        let sequences: [Sequence]
    }

    struct Failure: Decodable {
        let deviceName: String
        let reason: String
    }

    struct Transition: Decodable {
        let description: String
        let from: Value
        let event: Value
        let expected: Value
    }

    struct Sequence: Decodable {
        let description: String
        let from: Value
        let steps: [Step]
    }

    struct Step: Decodable {
        let event: Value
        let expected: Value
    }

    /// A state (`state`) or an event (`event`) with the fields its case needs.
    struct Value: Decodable {
        let state: String?
        let event: String?
        let permission: String?
        let devices: [String]?
        let device: String?
        let failure: String?
    }

    let spec: Spec

    init() throws {
        spec = try Fixture.decode(Spec.self, from: "launcher.json")
    }

    @Test func transitionsMatchTheSpec() throws {
        for transition in spec.transitions {
            let next = nextLauncherState(after: try state(transition.from), on: try event(transition.event))
            #expect(next == (try state(transition.expected)), "\(transition.description)")
        }
    }

    @Test func sequencesMatchTheSpec() throws {
        for sequence in spec.sequences {
            var current = try state(sequence.from)
            for (index, step) in sequence.steps.enumerated() {
                current = nextLauncherState(after: current, on: try event(step.event))
                #expect(current == (try state(step.expected)), "\(sequence.description), step \(index + 1)")
            }
        }
    }

    private func state(_ value: Value) throws -> LauncherState {
        switch try require(value.state, "state") {
        case "permissionNeeded": .permissionNeeded(try permission(value))
        case "permissionDenied": .permissionDenied(try permission(value))
        case "waitingForSignal": .waitingForSignal
        case "ready": .ready(try spec.devices.devices(require(value.devices, "devices")))
        case "playing": .playing(try spec.devices.device(require(value.device, "device")))
        case "failed": .failed(try failure(value))
        case let name: throw FixtureError.unknownValue("state \(name)")
        }
    }

    private func event(_ value: Value) throws -> LauncherEvent {
        switch try require(value.event, "event") {
        case "permissionRequested": .permissionRequested(try permission(value))
        case "permissionDenied": .permissionDenied(try permission(value))
        case "permissionsGranted": .permissionsGranted
        case "devicesChanged": .devicesChanged(try spec.devices.devices(require(value.devices, "devices")))
        case "playbackStarted": .playbackStarted(try spec.devices.device(require(value.device, "device")))
        case "playbackFailed": .playbackFailed(try failure(value))
        case "playbackStopped": .playbackStopped
        case let name: throw FixtureError.unknownValue("event \(name)")
        }
    }

    private func permission(_ value: Value) throws -> PermissionKind {
        switch try require(value.permission, "permission") {
        case "camera": .camera
        case "microphone": .microphone
        case let name: throw FixtureError.unknownValue("permission \(name)")
        }
    }

    private func failure(_ value: Value) throws -> CaptureFailure {
        _ = try require(value.failure, "failure")
        return CaptureFailure(deviceName: spec.failure.deviceName, reason: spec.failure.reason)
    }

    private func require<Field>(_ field: Field?, _ name: String) throws -> Field {
        guard let field else { throw FixtureError.missingField(name) }
        return field
    }
}
