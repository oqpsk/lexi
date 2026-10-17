// swift-tools-version: 6.2
import Foundation
import PackageDescription

// The Simulator reads an app's entitlements from a section of its binary, as Xcode embeds them; the
// Keychain refuses an app without them. A signature carrying entitlements stops the app launching.
let entitlements = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appending(path: "Resources/LeXi.entitlements").path

// LeXi for iOS (simulator preview). The app shares LeXiKit with macOS and runs Lexi.Core inside its
// own process as a NativeAOT library (Lexi.Embed), since iOS cannot start the lexi-host process.
// Build and install with apple/scripts/build-ios.sh.
let package = Package(
    name: "LeXiMobile",
    defaultLocalization: "zh-Hans",
    platforms: [.iOS(.v26)],
    dependencies: [.package(name: "LeXi", path: "..")],
    targets: [
        .executableTarget(name: "LeXiMobile", dependencies: [.product(name: "LeXiKit", package: "LeXi")], linkerSettings: [
            .unsafeFlags(["-Xlinker", "-sectcreate", "-Xlinker", "__TEXT", "-Xlinker", "__entitlements", "-Xlinker", entitlements]),
        ]),
    ]
)
