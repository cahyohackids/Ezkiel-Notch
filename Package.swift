// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "EzkielNotch",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "EzkielNotch", targets: ["EzkielNotch"])
    ],
    targets: [
        .target(name: "NotchCore"),
        .executableTarget(
            name: "EzkielNotch",
            dependencies: ["NotchCore"]
        ),
        .testTarget(
            name: "NotchCoreTests",
            dependencies: ["NotchCore"]
        )
    ]
)
