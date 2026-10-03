import Foundation
import PlugviewCore

/// Reads the shared fixtures in `spec/fixtures`: the behavior every platform's port must match.
enum Fixture {
    static let directory = URL(filePath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appending(path: "spec/fixtures")

    static func data(_ name: String) throws -> Data {
        try Data(contentsOf: directory.appending(path: name))
    }

    static func decode<Value: Decodable>(_ type: Value.Type, from name: String) throws -> Value {
        try JSONDecoder().decode(type, from: data(name))
    }

    /// The fixture as plain JSON objects, for cases that must tell a missing field from an explicit `null`.
    static func object(_ name: String) throws -> [String: Any] {
        guard let object = try JSONSerialization.jsonObject(with: data(name)) as? [String: Any] else { throw FixtureError.notAnObject(name) }
        return object
    }
}

enum FixtureError: Error {
    case notAnObject(String)
    case unknownValue(String)
    case missingField(String)
}

/// A device as fixtures write it; `transport` uses the `DeviceTransport` case names.
struct FixtureDevice: Decodable {
    let uniqueID: String
    let name: String
    let modelID: String
    let transport: String

    func device() throws -> DeviceInfo {
        DeviceInfo(uniqueID: uniqueID, name: name, modelID: modelID, transport: try Self.transport(named: transport))
    }

    private static func transport(named name: String) throws -> DeviceTransport {
        switch name {
        case "builtIn": .builtIn
        case "usb": .usb
        case "virtual": .virtual
        case "wireless": .wireless
        case "other": .other
        default: throw FixtureError.unknownValue("transport \(name)")
        }
    }
}

/// Fixture devices by key, so cases can name them.
struct FixtureDevices: Decodable {
    let devices: [String: FixtureDevice]

    init(from decoder: any Decoder) throws {
        devices = try [String: FixtureDevice](from: decoder)
    }

    func device(_ key: String) throws -> DeviceInfo {
        guard let device = devices[key] else { throw FixtureError.unknownValue("device \(key)") }
        return try device.device()
    }

    func devices(_ keys: [String]) throws -> [DeviceInfo] {
        try keys.map(device)
    }
}

/// A capture format as fixtures write it; `encoding` uses the `FrameEncoding` case names.
struct FixtureMode: Decodable {
    let width: Int
    let height: Int
    let maxFrameRate: Double
    let encoding: String

    func mode() throws -> VideoMode {
        VideoMode(width: width, height: height, maxFrameRate: maxFrameRate, encoding: try Self.encoding(named: encoding))
    }

    private static func encoding(named name: String) throws -> FrameEncoding {
        switch name {
        case "compressed": .compressed
        case "converted": .converted
        case "native": .native
        default: throw FixtureError.unknownValue("encoding \(name)")
        }
    }
}
