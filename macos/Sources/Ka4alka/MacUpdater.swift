import AppKit
import CryptoKit
import Darwin
import Foundation
import Security

struct MacPackage: Codable { var url: String; var fileName: String; var size: Int64; var sha256: String }
struct MacManifest: Codable {
    var schemaVersion: Int; var appId: String; var channel: String; var version: String; var platform: String; var minimumMacOS: String; var packages: [String: MacPackage]; var releaseNotesUrl: String
    static func validate(_ data: Data, signature: Data, publicKey: String) throws -> MacManifest {
        guard data.count <= 64 * 1024, signature.count <= 1024, let key = publicRSAKey(publicKey), SecKeyVerifySignature(key, .rsaSignatureMessagePKCS1v15SHA256, data as CFData, signature as CFData, nil) else { throw UpdateError("Invalid update signature") }
        let value = try JSONDecoder().decode(MacManifest.self, from: data)
        guard value.schemaVersion == 1, value.appId == "kachalka", value.channel == "stable", value.platform == "macos", value.minimumMacOS == "14.0", value.version.range(of: #"^\d+\.\d+\.\d+$"#, options: .regularExpression) != nil, value.packages.count == 2, value.releaseNotesUrl == "https://github.com/peregond/ka4alka/releases/tag/v" + value.version else { throw UpdateError("Invalid update manifest") }
        for architecture in ["arm64", "x64"] {
            guard let package = value.packages[architecture], package.fileName == "Kachalka-macos-\(architecture).zip", package.url == "https://github.com/peregond/ka4alka/releases/download/v\(value.version)/\(package.fileName)", package.size > 0, package.size <= 512 * 1024 * 1024, package.sha256.range(of: #"^[a-f0-9]{64}$"#, options: .regularExpression) != nil else { throw UpdateError("Invalid update package") }
        }
        return value
    }
    static func newer(_ candidate: String, than installed: String) -> Bool { candidate.compare(installed, options: .numeric) == .orderedDescending }
}
struct UpdateError: LocalizedError { var errorDescription: String?; init(_ text: String) { errorDescription = text } }
// Security imports the inner PKCS#1 RSA key, not its SubjectPublicKeyInfo wrapper.
func publicRSAKey(_ pem: String) -> SecKey? {
    let base64 = pem.split(separator: "\n").filter { !$0.hasPrefix("---") }.joined()
    guard let der = Data(base64Encoded: base64) else { return nil }
    let bytes = [UInt8](der)
    func element(_ start: Int) -> (tag: UInt8, body: Range<Int>, end: Int)? {
        guard start >= 0, start + 2 <= bytes.count else { return nil }
        let tag = bytes[start]; let first = Int(bytes[start + 1]); var offset = start + 2; var length = first
        if first & 0x80 != 0 {
            let count = first & 0x7f; guard count > 0, count <= 4, offset + count <= bytes.count else { return nil }; length = 0
            for _ in 0..<count { length = length * 256 + Int(bytes[offset]); offset += 1 }
        }
        guard length >= 0, length <= bytes.count - offset else { return nil }
        return (tag, offset..<(offset + length), offset + length)
    }
    var raw = der
    if let outer = element(0), outer.tag == 0x30, let algorithm = element(outer.body.lowerBound), algorithm.tag == 0x30, let bits = element(algorithm.end), bits.tag == 0x03, bits.body.count > 1, bytes[bits.body.lowerBound] == 0 { raw = Data(bytes[(bits.body.lowerBound + 1)..<bits.body.upperBound]) }
    return SecKeyCreateWithData(raw as CFData, [kSecAttrKeyType: kSecAttrKeyTypeRSA, kSecAttrKeyClass: kSecAttrKeyClassPublic] as CFDictionary, nil)
}
func sha256File(_ url: URL) throws -> String {
    let file = try FileHandle(forReadingFrom: url); defer { try? file.close() }; var hash = SHA256()
    while let data = try file.read(upToCount: 1024 * 1024), !data.isEmpty { hash.update(data: data) }
    return hash.finalize().map { String(format: "%02x", $0) }.joined()
}
@discardableResult func runTool(_ executable: String, _ args: [String]) throws -> String {
    let process = Process(); let pipe = Pipe(); process.executableURL = URL(fileURLWithPath: executable); process.arguments = args; process.standardOutput = pipe; process.standardError = pipe
    try process.run(); let data = pipe.fileHandleForReading.readDataToEndOfFile(); process.waitUntilExit()
    guard process.terminationStatus == 0 else { throw UpdateError("\(URL(fileURLWithPath: executable).lastPathComponent) failed: " + String(data: data.suffix(2048), encoding: .utf8)!) }
    return String(data: data, encoding: .utf8) ?? ""
}
struct MacUpdateJob: Codable { var installed: String; var archive: String; var manifest: String; var signature: String; var publicKey: String; var parent: Int32; var architecture: String }
@MainActor final class MacUpdater: ObservableObject {
    @Published var statusText = ""
    @Published var busy = false
    @Published var available = false
    @Published var ready = false
    private var manifest: MacManifest?
    private var manifestData: Data?
    private var signatureData: Data?
    private var archive: URL?
    static let feed = URL(string: "https://github.com/peregond/ka4alka/releases/latest/download/macos-latest.json")!
    static var architecture: String {
#if arch(arm64)
        return "arm64"
#else
        return "x64"
#endif
    }
    var buttonTitle: String { ready ? tr("Обновить и перезапустить", "Update and restart") : busy ? tr("Загрузка…", "Loading…") : available ? tr("Скачать обновление", "Download update") : tr("Проверить обновления", "Check for updates") }
    var installedVersion: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0" }
    func primaryAction() async { if ready { do { try install() } catch { statusText = error.localizedDescription } } else if available { await download() } else { await check() } }
    func check(autoDownload: Bool = false) async {
        guard !busy else { return }; busy = true; defer { busy = false }
        do {
            let (data, response) = try await URLSession.shared.data(from: Self.feed)
            // Older Windows-only releases have no macOS manifest.
            if (response as? HTTPURLResponse)?.statusCode == 404 { statusText = tr("Установлена актуальная версия", "You are up to date"); return }
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw UpdateError("Update server unavailable") }
            let (signature, sigResponse) = try await URLSession.shared.data(from: Self.feed.deletingLastPathComponent().appendingPathComponent("macos-latest.sig"))
            guard (sigResponse as? HTTPURLResponse)?.statusCode == 200, let keyURL = Bundle.main.url(forResource: "update-public", withExtension: "pem") else { throw UpdateError("Update signature unavailable") }
            let value = try MacManifest.validate(data, signature: signature, publicKey: String(contentsOf: keyURL))
            manifest = value; manifestData = data; signatureData = signature
            available = MacManifest.newer(value.version, than: installedVersion)
            statusText = available ? tr("Доступна версия ", "Version available: ") + value.version : tr("Установлена актуальная версия", "You are up to date")
            if autoDownload, available { busy = false; await download() }
        } catch { statusText = error.localizedDescription }
    }
    func download() async {
        guard !busy, let manifest, let package = manifest.packages[Self.architecture], let url = URL(string: package.url) else { return }
        busy = true; statusText = tr("Скачиваем обновление…", "Downloading update…"); defer { busy = false }
        do {
            let (temporary, response) = try await URLSession.shared.download(from: url)
            guard (response as? HTTPURLResponse)?.statusCode == 200, (try temporary.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0) == package.size else { throw UpdateError("Update package size mismatch") }
            let hash = try await Task.detached(priority: .utility) { try sha256File(temporary) }.value
            guard hash == package.sha256 else { throw UpdateError("Update package checksum mismatch") }
            let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Kachalka/updates/" + UUID().uuidString)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let path = directory.appendingPathComponent(package.fileName); try FileManager.default.moveItem(at: temporary, to: path)
            try manifestData!.write(to: directory.appendingPathComponent("manifest.json"), options: .atomic); try signatureData!.write(to: directory.appendingPathComponent("manifest.sig"), options: .atomic)
            archive = path; ready = true; statusText = tr("Обновление готово к установке", "Update ready to install")
        } catch { statusText = error.localizedDescription }
    }
    func install() throws {
        guard ready, let archive, let keyURL = Bundle.main.url(forResource: "update-public", withExtension: "pem"), let executable = Bundle.main.executableURL else { throw UpdateError("Update is not ready") }
        let directory = archive.deletingLastPathComponent(); let helper = directory.appendingPathComponent("Ka4alkaUpdate")
        try FileManager.default.copyItem(at: executable, to: helper); try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: helper.path)
        let job = MacUpdateJob(installed: Bundle.main.bundlePath, archive: archive.path, manifest: directory.appendingPathComponent("manifest.json").path, signature: directory.appendingPathComponent("manifest.sig").path, publicKey: try String(contentsOf: keyURL), parent: getpid(), architecture: Self.architecture)
        let jobURL = directory.appendingPathComponent("job.json"); try JSONEncoder().encode(job).write(to: jobURL, options: .atomic)
        let process = Process(); process.executableURL = helper; process.arguments = ["--apply-update", jobURL.path]; try process.run(); NSApp.terminate(nil)
    }
}
// Runs from a temporary copy, after the app has saved its queue and exited.
enum MacUpdateInstaller {
    static func apply(_ jobURL: URL, reopen: Bool = true) throws {
        let files = FileManager.default; let job = try JSONDecoder().decode(MacUpdateJob.self, from: Data(contentsOf: jobURL))
        let manifest = try MacManifest.validate(Data(contentsOf: URL(fileURLWithPath: job.manifest)), signature: Data(contentsOf: URL(fileURLWithPath: job.signature)), publicKey: job.publicKey)
        guard let package = manifest.packages[job.architecture], try sha256File(URL(fileURLWithPath: job.archive)) == package.sha256, job.installed.hasSuffix("/Ka4alka.app") else { throw UpdateError("Invalid update job") }
        let old = URL(fileURLWithPath: job.installed); guard !old.resolvingSymlinksInPath().path.hasPrefix("/Volumes/") else { throw UpdateError("Move Ka4alka to Applications before updating") }
        for _ in 0..<300 { if job.parent <= 0 || kill(job.parent, 0) != 0 { break }; Thread.sleep(forTimeInterval: 0.1) }
        guard job.parent <= 0 || kill(job.parent, 0) != 0 else { throw UpdateError("The application did not exit") }
        let listing = try runTool("/usr/bin/unzip", ["-Z1", job.archive]).split(separator: "\n")
        guard listing.count > 0, listing.count <= 4000, listing.allSatisfy({ $0.hasPrefix("Ka4alka.app/") && !$0.split(separator: "/").contains("..") && !$0.contains("\\") }) else { throw UpdateError("Unsafe update archive layout") }
        let staging = jobURL.deletingLastPathComponent().appendingPathComponent("staging")
        try files.createDirectory(at: staging, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try runTool("/usr/bin/ditto", ["-x", "-k", job.archive, staging.path])
        let app = staging.appendingPathComponent("Ka4alka.app")
        guard let bundle = Bundle(url: app), bundle.bundleIdentifier == "org.ka4alka.app", bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == manifest.version else { throw UpdateError("Unexpected application identity") }
        try runTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
        let backup = old.deletingLastPathComponent().appendingPathComponent("Ka4alka-previous-" + UUID().uuidString + ".app")
        try files.moveItem(at: old, to: backup)
        do { try files.moveItem(at: app, to: old) } catch { try? files.moveItem(at: backup, to: old); throw error }
        if reopen { try runTool("/usr/bin/open", ["-n", old.path]) }
        try? files.removeItem(at: backup)
    }
    static func run(_ path: String) -> Int32 { do { try apply(URL(fileURLWithPath: path)); return 0 } catch { try? error.localizedDescription.write(toFile: path + ".error.txt", atomically: true, encoding: .utf8); return 1 } }
}
