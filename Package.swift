// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Tinysnap",
    platforms: [.macOS(.v14)],
    targets: [
        .target(name: "TinysnapCore"),
        .executableTarget(name: "TinysnapApp", dependencies: ["TinysnapCore"]),
        .testTarget(name: "TinysnapCoreTests", dependencies: ["TinysnapCore"]),
    ]
)
