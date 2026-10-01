// swift-tools-version: 6.0
import PackageDescription

// Embeds the usage strings macOS shows in the camera/microphone permission prompts.
let infoPlistPath = "\(Context.packageDirectory)/Sources/plugview/Resources/Info.plist"

// Swift 6 language mode (complete concurrency checking) plus explicit `any` for existential types.
let strictSettings: [SwiftSetting] = [.enableUpcomingFeature("ExistentialAny")]

let package = Package(
    name: "plugview",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "plugview", targets: ["plugview"])
    ],
    targets: [
        .target(name: "PlugviewCore", swiftSettings: strictSettings),
        .target(name: "PlugviewCapture", dependencies: ["PlugviewCore"], swiftSettings: strictSettings),
        .target(name: "PlugviewUI", dependencies: ["PlugviewCore"], swiftSettings: strictSettings),
        .executableTarget(
            name: "plugview",
            dependencies: ["PlugviewCore", "PlugviewCapture", "PlugviewUI"],
            exclude: ["Resources/Info.plist"],
            swiftSettings: strictSettings,
            linkerSettings: [
                .unsafeFlags(["-Xlinker", "-sectcreate", "-Xlinker", "__TEXT", "-Xlinker", "__info_plist", "-Xlinker", infoPlistPath])
            ]
        ),
        .testTarget(name: "PlugviewCoreTests", dependencies: ["PlugviewCore"], swiftSettings: strictSettings),
    ]
)
