// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "Ka4alka", platforms: [.macOS(.v14)], products: [.executable(name: "Ka4alka", targets: ["Ka4alka"])], targets: [.executableTarget(name: "Ka4alka"), .testTarget(name: "Ka4alkaTests", dependencies: ["Ka4alka"])])
