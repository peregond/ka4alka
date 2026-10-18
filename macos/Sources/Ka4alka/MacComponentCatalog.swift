import CryptoKit
import Foundation

struct MacComponent: Codable {
    var path: String; var size: Int64; var sha256: String; var offset: Int64; var packedSize: Int64; var method: Int; var mode: Int
}
struct MacComponentCatalog: Codable {
    var schemaVersion: Int; var version: String; var files: [MacComponent]
    static func safePath(_ path: String) -> Bool {
        let parts = path.split(separator: "/", omittingEmptySubsequences: false)
        // Foundation text search can ignore control scalars. Inspect bytes for
        // NUL and other controls instead of treating a pathname as display text.
        return path.hasPrefix("Contents/") && path.utf8.allSatisfy { $0 >= 32 && $0 != 127 && $0 != 92 }
            && !parts.contains { $0.isEmpty || $0 == "." || $0 == ".." || $0.contains(":") }
    }
    static func validate(_ data: Data, package: MacPackage, version: String, application: URL) throws {
        let hash = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        guard data.count == package.components.size, data.count <= 2 * 1024 * 1024, hash == package.components.sha256 else { throw UpdateError("Invalid component index checksum") }
        let index = try JSONDecoder().decode(Self.self, from: data)
        guard index.schemaVersion == 1, index.version == version, !index.files.isEmpty, index.files.count <= 4000 else { throw UpdateError("Invalid component index") }
        var paths = Set<String>(); var size: Int64 = 0
        let root = application.standardizedFileURL.path + "/"
        for file in index.files {
            guard safePath(file.path), paths.insert(file.path.lowercased()).inserted, file.size >= 0, file.size <= 512 * 1024 * 1024, file.packedSize >= 0, file.offset >= 0, file.offset <= package.size - file.packedSize, [0, 8].contains(file.method), [0o644, 0o755].contains(file.mode), file.sha256.range(of: #"^[a-f0-9]{64}$"#, options: .regularExpression) != nil else { throw UpdateError("Unsafe component path or size: \(file.path), mode \(file.mode)") }
            size += file.size; guard size <= 2 * 1024 * 1024 * 1024 else { throw UpdateError("Application exceeds size limit") }
            let path = application.appendingPathComponent(file.path).standardizedFileURL
            guard path.path.hasPrefix(root), path.resolvingSymlinksInPath().path == path.path else { throw UpdateError("Symlink in application components") }
            let properties = try path.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
            let attributes = try FileManager.default.attributesOfItem(atPath: path.path)
            guard properties.isRegularFile == true, let actualSize = properties.fileSize, Int64(actualSize) == file.size, (attributes[.posixPermissions] as? NSNumber)?.intValue == file.mode, try sha256File(path) == file.sha256 else { throw UpdateError("Component checksum or permissions mismatch") }
        }
        for path in paths {
            var parent = path
            while let slash = parent.lastIndex(of: "/") { parent = String(parent[..<slash]); guard !paths.contains(parent) else { throw UpdateError("Overlapping component paths") } }
        }
        for required in ["Contents/Info.plist", "Contents/MacOS/Ka4alka", "Contents/Resources/engine/Kachalka.Engine", "Contents/Resources/engine/Kachalka.Engine.dll", "Contents/Resources/engine/Kachalka.DesktopCore.dll", "Contents/Resources/update-public.pem"] {
            guard paths.contains(required.lowercased()) else { throw UpdateError("Missing application component") }
        }
        guard let enumerator = FileManager.default.enumerator(at: application, includingPropertiesForKeys: [.isRegularFileKey, .isSymbolicLinkKey]) else { throw UpdateError("Cannot inspect application") }
        for case let url as URL in enumerator {
            let properties = try url.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey])
            guard properties.isSymbolicLink != true else { throw UpdateError("Unexpected symlink") }
            if properties.isRegularFile == true { guard paths.contains(String(url.path.dropFirst(root.count)).lowercased()) else { throw UpdateError("Unexpected application component") } }
        }
    }
}
