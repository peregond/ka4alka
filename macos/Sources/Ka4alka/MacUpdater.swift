import AppKit
import CryptoKit
import Darwin
import Foundation
import Security

struct MacComponentIndex: Codable { var url: String; var size: Int64; var sha256: String }
struct MacPackage: Codable { var url: String; var fileName: String; var size: Int64; var sha256: String; var components: MacComponentIndex }
struct PreparedMacUpdate: Codable {
    var application: String; var catalogPath: String; var manifestPath: String; var signaturePath: String
    var downloadedBytes: Int64; var packageBytes: Int64; var reusedFiles: Int; var fullPackage: Bool
}
struct MacManifest: Codable {
    var schemaVersion: Int; var appId: String; var channel: String; var version: String; var platform: String; var minimumMacOS: String; var packages: [String: MacPackage]; var releaseNotesUrl: String
    static func validate(_ data: Data, signature: Data, publicKey: String) throws -> MacManifest {
        guard data.count <= 64 * 1024, signature.count <= 1024, let key = publicRSAKey(publicKey), SecKeyVerifySignature(key, .rsaSignatureMessagePKCS1v15SHA256, data as CFData, signature as CFData, nil) else { throw UpdateError("Invalid update signature") }
        let value = try JSONDecoder().decode(MacManifest.self, from: data)
        guard value.schemaVersion == 1, value.appId == "kachalka", value.channel == "stable", value.platform == "macos", value.minimumMacOS == "14.0", value.version.range(of: #"^\d+\.\d+\.\d+$"#, options: .regularExpression) != nil, value.packages.count == 2, value.releaseNotesUrl == "https://github.com/peregond/ka4alka/releases/tag/v" + value.version else { throw UpdateError("Invalid update manifest") }
        for architecture in ["arm64", "x64"] {
            guard let package = value.packages[architecture], package.fileName == "Kachalka-macos-\(architecture).zip", package.url == "https://github.com/peregond/ka4alka/releases/download/v\(value.version)/\(package.fileName)", package.size > 0, package.size <= 512 * 1024 * 1024, package.components.url == "https://github.com/peregond/ka4alka/releases/download/v\(value.version)/macos-components-\(architecture).json", package.components.size > 0, package.components.size <= 2 * 1024 * 1024, package.components.sha256.range(of: #"^[a-f0-9]{64}$"#, options: .regularExpression) != nil, package.sha256.range(of: #"^[a-f0-9]{64}$"#, options: .regularExpression) != nil else { throw UpdateError("Invalid update package") }
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
    guard process.terminationStatus == 0 else { throw UpdateError("\(URL(fileURLWithPath: executable).lastPathComponent) failed: " + (String(data: data.suffix(2048), encoding: .utf8) ?? "unknown error")) }
    return String(data: data, encoding: .utf8) ?? ""
}
struct MacUpdateJob: Codable {
    var installed: String; var application: String; var catalog: String; var manifest: String
    var signature: String; var publicKey: String; var parent: Int32; var architecture: String
}
@MainActor final class MacUpdater: ObservableObject {
    @Published var statusText = ""
    @Published var busy = false
    @Published var available = false
    @Published var ready = false
    private var manifest: MacManifest?
    private var manifestData: Data?
    private var signatureData: Data?
    private var prepared: PreparedMacUpdate?
    var engine: EngineBridge?
    private(set) var installing = false
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
    func progress(_ data: JSONValue) {
        let total = data["total"].number
        guard busy, total > 0 else { return }
        let percent = min(100, Int(data["received"].number * 100 / total))
        statusText = tr("Скачиваем обновление…", "Downloading update…") + " \(percent)%"
    }
    func download() async {
        guard !busy, manifest != nil, let manifestData, let signatureData, let engine else { return }
        busy = true; statusText = tr("Проверяем компоненты…", "Checking components…"); defer { busy = false }
        do {
            prepared = try await engine.call("prepareUpdate", ["manifest": .string(manifestData.base64EncodedString()), "signature": .string(signatureData.base64EncodedString()), "architecture": .string(Self.architecture), "installed": .string(Bundle.main.bundlePath)], as: PreparedMacUpdate.self)
            ready = true
            statusText = tr("Обновление готово к установке", "Update ready to install")
            if let prepared { statusText += " · " + byteText(Double(prepared.downloadedBytes)) + " · " + tr("Сохранено компонентов: ", "Reused components: ") + String(prepared.reusedFiles) }
        } catch { statusText = error.localizedDescription }
    }
    func install(terminate: Bool = true) throws {
        guard !installing, ready, let prepared, let keyURL = Bundle.main.url(forResource: "update-public", withExtension: "pem"), let executable = Bundle.main.executableURL else { throw UpdateError("Update is not ready") }
        let installed = URL(fileURLWithPath: Bundle.main.bundlePath)
        guard FileManager.default.isWritableFile(atPath: installed.deletingLastPathComponent().path) else { throw UpdateError(tr("Перемести Качалку в доступную для записи папку Applications.", "Move Ka4alka to a writable Applications folder before updating.")) }
        let directory = URL(fileURLWithPath: prepared.manifestPath).deletingLastPathComponent()
        let helper = directory.appendingPathComponent("Ka4alkaUpdate-" + UUID().uuidString)
        try FileManager.default.copyItem(at: executable, to: helper)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: helper.path)
        let job = MacUpdateJob(installed: installed.path, application: prepared.application, catalog: prepared.catalogPath, manifest: prepared.manifestPath, signature: prepared.signaturePath, publicKey: try String(contentsOf: keyURL), parent: getpid(), architecture: Self.architecture)
        let jobURL = directory.appendingPathComponent("job.json"); try JSONEncoder().encode(job).write(to: jobURL, options: .atomic)
        let process = Process(); process.executableURL = helper; process.arguments = ["--apply-update", jobURL.path]; try process.run()
        installing = true
        if terminate { NSApp.terminate(nil) }
    }

}
// Runs from a temporary copy, after the app has saved its queue and exited.
enum MacUpdateInstaller {
    static func apply(_ jobURL: URL, reopen: Bool = true) throws {
        let files = FileManager.default; let job = try JSONDecoder().decode(MacUpdateJob.self, from: Data(contentsOf: jobURL))
        let manifest = try MacManifest.validate(Data(contentsOf: URL(fileURLWithPath: job.manifest)), signature: Data(contentsOf: URL(fileURLWithPath: job.signature)), publicKey: job.publicKey)
        guard let package = manifest.packages[job.architecture], job.installed.hasSuffix("/Ka4alka.app") else { throw UpdateError("Invalid update job") }
        let old = URL(fileURLWithPath: job.installed)
        guard files.isWritableFile(atPath: old.deletingLastPathComponent().path), try old.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true else { throw UpdateError("The application folder is not writable") }
        let app = URL(fileURLWithPath: job.application)
        try MacComponentCatalog.validate(Data(contentsOf: URL(fileURLWithPath: job.catalog)), package: package, version: manifest.version, application: app)
        guard let bundle = Bundle(url: app), bundle.bundleIdentifier == "org.ka4alka.app", bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == manifest.version else { throw UpdateError("Unexpected application identity") }
        try runTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
        for _ in 0..<300 { if job.parent <= 0 || kill(job.parent, 0) != 0 { break }; Thread.sleep(forTimeInterval: 0.1) }
        guard job.parent <= 0 || kill(job.parent, 0) != 0 else { throw UpdateError("The application did not exit") }
        let backup = old.deletingLastPathComponent().appendingPathComponent("Ka4alka-previous-" + UUID().uuidString + ".app")
        try files.moveItem(at: old, to: backup)
        do { try files.moveItem(at: app, to: old) } catch { try? files.moveItem(at: backup, to: old); throw error }
        do {
            if reopen {
                let health = jobURL.deletingLastPathComponent().appendingPathComponent("health-" + UUID().uuidString)
                let token = UUID().uuidString
                let child = Process(); child.executableURL = old.appendingPathComponent("Contents/MacOS/Ka4alka")
                var environment = ProcessInfo.processInfo.environment
                environment["KACHALKA_UPDATE_HEALTH_PATH"] = health.path; environment["KACHALKA_UPDATE_HEALTH_TOKEN"] = token
                child.environment = environment; try child.run()
                var healthy = false
                for _ in 0..<300 {
                    if let data = try? Data(contentsOf: health), let value = try? JSONDecoder().decode([String: String].self, from: data), value["token"] == token, value["version"] == manifest.version { healthy = true; break }
                    if !child.isRunning { break }; Thread.sleep(forTimeInterval: 0.1)
                }
                try? files.removeItem(at: health)
                if !healthy { if child.isRunning { child.terminate(); child.waitUntilExit() }; throw UpdateError("The updated application failed its startup check; restored the previous version") }
            }
            try? files.removeItem(at: backup)
        } catch {
            try? files.removeItem(at: old); try files.moveItem(at: backup, to: old)
            if reopen { try? runTool("/usr/bin/open", ["-n", old.path]) }
            throw error
        }
    }
    static func run(_ path: String) -> Int32 { do { try apply(URL(fileURLWithPath: path)); return 0 } catch { try? error.localizedDescription.write(toFile: path + ".error.txt", atomically: true, encoding: .utf8); return 1 } }
}
