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
        guard data.count <= 64 * 1024, signature.count <= 1024, let key = publicRSAKey(publicKey), SecKeyVerifySignature(key, .rsaSignatureMessagePKCS1v15SHA256, data as CFData, signature as CFData, nil) else { throw UpdateError(tr("Подпись обновления недействительна.", "The update signature is invalid.")) }
        let value = try JSONDecoder().decode(MacManifest.self, from: data)
        guard value.schemaVersion == 1, value.appId == "kachalka", value.channel == "stable", value.platform == "macos", value.minimumMacOS == "14.0", value.version.range(of: #"^\d+\.\d+\.\d+$"#, options: .regularExpression) != nil, value.packages.count == 2, value.releaseNotesUrl == "https://github.com/peregond/ka4alka/releases/tag/v" + value.version else { throw UpdateError(tr("Описание обновления недействительно.", "The update manifest is invalid.")) }
        for architecture in ["arm64", "x64"] {
            guard let package = value.packages[architecture], package.fileName == "Kachalka-macos-\(architecture).zip", package.url == "https://github.com/peregond/ka4alka/releases/download/v\(value.version)/\(package.fileName)", package.size > 0, package.size <= 512 * 1024 * 1024, package.components.url == "https://github.com/peregond/ka4alka/releases/download/v\(value.version)/macos-components-\(architecture).json", package.components.size > 0, package.components.size <= 2 * 1024 * 1024, package.components.sha256.range(of: #"^[a-f0-9]{64}$"#, options: .regularExpression) != nil, package.sha256.range(of: #"^[a-f0-9]{64}$"#, options: .regularExpression) != nil else { throw UpdateError(tr("Пакет обновления недействителен.", "The update package is invalid.")) }
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
    guard process.terminationStatus == 0 else { let name = URL(fileURLWithPath: executable).lastPathComponent; let output = String(data: data.suffix(2048), encoding: .utf8) ?? tr("неизвестная ошибка", "unknown error"); throw UpdateError(tr("Ошибка «\(name)»: ", "\(name) failed: ") + output) }
    return String(data: data, encoding: .utf8) ?? ""
}
struct MacUpdateJob: Codable {
    var installed: String; var application: String; var catalog: String; var manifest: String
    var signature: String; var publicKey: String; var parent: Int32; var architecture: String
    /// False when the person quit the app on purpose: the helper then installs without launching the new version.
    var reopen: Bool? = nil
}
private let updatesRootKey = "Ka4alkaUpdatesRoot"
private let updateFailurePathKey = "Ka4alkaUpdateFailurePath"
@MainActor final class MacUpdater: ObservableObject {
    @Published var statusText = ""
    @Published var busy = false
    @Published var available = false
    @Published var ready = false
    /// True while `statusText` describes a failure.
    @Published var isError = false
    /// Download progress 0...1 while the engine reports sizes; nil otherwise.
    @Published var progressValue: Double?
    /// The reason the last install did not happen, set by `consumeInstallFailure()` and cleared when `install()` starts. `check()` leaves it alone.
    @Published var installFailure: String?
    private var manifest: MacManifest?
    private var manifestData: Data?
    private var signatureData: Data?
    private var prepared: PreparedMacUpdate?
    var engine: EngineBridge?
    private(set) var installing = false
    static let feed = URL(string: "https://github.com/peregond/ka4alka/releases/latest/download/macos-latest.json")!
    nonisolated static var architecture: String {
#if arch(arm64)
        return "arm64"
#else
        return "x64"
#endif
    }
    var buttonTitle: String { ready ? tr("Обновить", "Update") : busy ? tr("Загрузка…", "Loading…") : available ? tr("Скачать обновление", "Download update") : tr("Проверить обновления", "Check for updates") }
    private func setStatus(_ text: String, error: Bool = false) { statusText = text; isError = error }
    private func setFailure(_ error: Error) { setStatus(error.localizedDescription, error: true) }
    /// Forgets a prepared update that can no longer be installed, so the button offers to download it again.
    private func discardPrepared() {
        ready = false
        prepared = nil
        UserDefaults.standard.removeObject(forKey: "Ka4alkaPreparedUpdate")
    }
    var installedVersion: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0" }
    func restore() async {
        guard !busy, !ready else { return }
        let stored = UserDefaults.standard.data(forKey: "Ka4alkaPreparedUpdate")
        var candidate: PreparedMacUpdate?
        if let stored, stored.count < 16384 { candidate = try? JSONDecoder().decode(PreparedMacUpdate.self, from: stored) }
        pruneStaleUpdates(keeping: candidate.map { URL(fileURLWithPath: $0.manifestPath).deletingLastPathComponent() })
        guard let prepared = candidate else {
            if stored != nil { UserDefaults.standard.removeObject(forKey: "Ka4alkaPreparedUpdate") }
            return
        }
        do {
            guard let keyURL = Bundle.main.url(forResource: "update-public", withExtension: "pem") else { return }
            let publicKey = try String(contentsOf: keyURL)
            let restored = try await Task.detached(priority: .utility) {
                let bytes = try Data(contentsOf: URL(fileURLWithPath: prepared.manifestPath))
                let signature = try Data(contentsOf: URL(fileURLWithPath: prepared.signaturePath))
                let manifest = try MacManifest.validate(bytes, signature: signature, publicKey: publicKey)
                guard let package = manifest.packages[Self.architecture] else { throw UpdateError(tr("Обновление не подходит для этого процессора.", "The update does not support this processor.")) }
                try MacComponentCatalog.validate(Data(contentsOf: URL(fileURLWithPath: prepared.catalogPath)), package: package, version: manifest.version, application: URL(fileURLWithPath: prepared.application))
                return (manifest, bytes, signature)
            }.value
            guard MacManifest.newer(restored.0.version, than: installedVersion) else { UserDefaults.standard.removeObject(forKey: "Ka4alkaPreparedUpdate"); return }
            self.prepared = prepared; manifest = restored.0; manifestData = restored.1; signatureData = restored.2
            available = true; ready = true; setStatus(tr("Обновление готово к установке", "Update ready to install"))
        } catch { UserDefaults.standard.removeObject(forKey: "Ka4alkaPreparedUpdate") }
    }
    /// Deletes prepared-update directories other than the one in use. Only `<updates>/<32 hex digits>` folders are ever touched.
    private func pruneStaleUpdates(keeping keep: URL?) {
        // The update helper is still verifying this launch and reads its own folder in there; the next normal launch cleans up.
        if ProcessInfo.processInfo.environment["KACHALKA_UPDATE_HEALTH_PATH"] != nil { return }
        var root: URL?
        if let keep { root = keep.deletingLastPathComponent() }
        else if let stored = UserDefaults.standard.string(forKey: updatesRootKey) { root = URL(fileURLWithPath: stored) }
        guard let root else { return }
        UserDefaults.standard.set(root.path, forKey: updatesRootKey)
        Task.detached(priority: .utility) { MacUpdater.pruneUpdateDirectories(root: root, keeping: keep) }
    }
    nonisolated static func pruneUpdateDirectories(root: URL, keeping keep: URL?) {
        guard root.lastPathComponent == "updates" else { return }
        let files = FileManager.default
        guard let children = try? files.contentsOfDirectory(at: root, includingPropertiesForKeys: [.isDirectoryKey, .isSymbolicLinkKey, .contentModificationDateKey], options: []) else { return }
        let recent = Date().addingTimeInterval(-600)
        let kept = keep?.resolvingSymlinksInPath().standardizedFileURL.path
        for child in children {
            guard child.lastPathComponent.range(of: #"^[0-9a-fA-F]{32}$"#, options: .regularExpression) != nil else { continue }
            let values = try? child.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey, .contentModificationDateKey])
            guard values?.isDirectory == true, values?.isSymbolicLink != true else { continue }
            // A folder touched in the last ten minutes may belong to an install or a download that is still running.
            if let modified = values?.contentModificationDate, modified > recent { continue }
            if child.resolvingSymlinksInPath().standardizedFileURL.path == kept { continue }
            try? files.removeItem(at: child)
        }
    }
    /// The reason the last install did not happen (written by the helper, or by the quit flow), shown once. Nil when there is none.
    func consumeInstallFailure() -> String? {
        let defaults = UserDefaults.standard
        var found: String?
        if let path = defaults.string(forKey: updateFailurePathKey) {
            let file = URL(fileURLWithPath: path)
            if let text = try? String(contentsOf: file, encoding: .utf8) {
                let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
                if !trimmed.isEmpty { found = String(trimmed.prefix(600)) }
            }
            try? FileManager.default.removeItem(at: file)
            defaults.removeObject(forKey: updateFailurePathKey)
        }
        if let legacy = defaults.string(forKey: "Ka4alkaUpdateError") {
            defaults.removeObject(forKey: "Ka4alkaUpdateError")
            if found == nil, !legacy.isEmpty { found = legacy }
        }
        guard let reason = found else { return nil }
        let text = tr("Обновление не установлено: ", "The update was not installed: ") + reason
        setStatus(text, error: true)
        installFailure = text
        return text
    }
    func primaryAction() async {
        if ready {
            do { try await preflight(); try install() } catch { setFailure(error) }
        } else if available { await download() } else { await check() }
    }
    /// Checks the prepared application's signature before anything is quit or replaced.
    func preflight() async throws {
        guard ready, let prepared else { throw UpdateError(tr("Обновление не готово к установке.", "The update is not ready to install.")) }
        let application = URL(fileURLWithPath: prepared.application)
        guard FileManager.default.fileExists(atPath: application.path) else {
            discardPrepared()
            throw UpdateError(tr("Файлы обновления не найдены. Скачай обновление заново.", "The update files are missing. Download the update again."))
        }
        do {
            try await Task.detached(priority: .userInitiated) { () throws -> Void in
                _ = try runTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", application.path])
            }.value
        } catch {
            discardPrepared()
            throw error
        }
    }
    func check(autoDownload: Bool = false) async {
        guard !busy, !ready else { return }; busy = true; defer { busy = false }
        setStatus(tr("Проверяем обновления…", "Checking for updates…"))
        do {
            let (data, response) = try await URLSession.shared.data(from: Self.feed)
            // Older Windows-only releases have no macOS manifest.
            if (response as? HTTPURLResponse)?.statusCode == 404 { setStatus(tr("Установлена актуальная версия", "You are up to date")); return }
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw UpdateError(tr("Сервер обновлений недоступен.", "The update server is unavailable.")) }
            let (signature, sigResponse) = try await URLSession.shared.data(from: Self.feed.deletingLastPathComponent().appendingPathComponent("macos-latest.sig"))
            guard (sigResponse as? HTTPURLResponse)?.statusCode == 200, let keyURL = Bundle.main.url(forResource: "update-public", withExtension: "pem") else { throw UpdateError(tr("Подпись обновления недоступна.", "The update signature is unavailable.")) }
            let value = try MacManifest.validate(data, signature: signature, publicKey: String(contentsOf: keyURL))
            manifest = value; manifestData = data; signatureData = signature
            available = MacManifest.newer(value.version, than: installedVersion)
            setStatus(available ? tr("Доступна версия ", "Version available: ") + value.version : tr("Установлена актуальная версия", "You are up to date"))
            if autoDownload, available { busy = false; await download() }
        } catch { setFailure(error) }
    }
    func progress(_ data: JSONValue) {
        let total = data["total"].number
        guard busy, total > 0 else { return }
        let fraction = min(1, max(0, data["received"].number / total))
        progressValue = fraction
        statusText = tr("Скачиваем обновление…", "Downloading update…") + " \(Int(fraction * 100))%"
    }
    func download() async {
        guard !busy, manifest != nil, let manifestData, let signatureData, let engine else { return }
        busy = true; progressValue = nil; setStatus(tr("Проверяем компоненты…", "Checking components…")); defer { busy = false; progressValue = nil }
        do {
            prepared = try await engine.call("prepareUpdate", ["manifest": .string(manifestData.base64EncodedString()), "signature": .string(signatureData.base64EncodedString()), "architecture": .string(Self.architecture), "installed": .string(Bundle.main.bundlePath)], as: PreparedMacUpdate.self)
            UserDefaults.standard.set(try JSONEncoder().encode(prepared), forKey: "Ka4alkaPreparedUpdate")
            ready = true
            setStatus(tr("Обновление готово к установке", "Update ready to install"))
            if let prepared {
                statusText += " · " + byteText(Double(prepared.downloadedBytes)) + " · " + tr("Сохранено компонентов: ", "Reused components: ") + String(prepared.reusedFiles)
                pruneStaleUpdates(keeping: URL(fileURLWithPath: prepared.manifestPath).deletingLastPathComponent())
            }
        } catch { setFailure(error) }
    }
    /// Starts the helper that replaces the application after this process exits. Throws, without quitting, when the install cannot work.
    /// `terminate` is true for "Update and restart" (the new version is launched and checked); false when the person is quitting.
    func install(terminate: Bool = true) throws {
        installFailure = nil
        guard !installing, ready, let prepared, let keyURL = Bundle.main.url(forResource: "update-public", withExtension: "pem"), let executable = Bundle.main.executableURL else { throw UpdateError(tr("Обновление не готово к установке.", "The update is not ready to install.")) }
        let installed = URL(fileURLWithPath: Bundle.main.bundlePath)
        guard installed.lastPathComponent == "Ka4alka.app" else { throw UpdateError(tr("Обновить можно только приложение Ka4alka.app.", "Only Ka4alka.app can be updated.")) }
        guard (try? installed.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink) != true else { throw UpdateError(tr("Ka4alka.app не должно быть ссылкой.", "Ka4alka.app must not be a symbolic link.")) }
        guard FileManager.default.isWritableFile(atPath: installed.deletingLastPathComponent().path) else { throw UpdateError(tr("Перемести Качалку в доступную для записи папку Applications.", "Move Ka4alka to a writable Applications folder before updating.")) }
        guard FileManager.default.fileExists(atPath: prepared.application) else {
            discardPrepared()
            throw UpdateError(tr("Файлы обновления не найдены. Скачай обновление заново.", "The update files are missing. Download the update again."))
        }
        let directory = URL(fileURLWithPath: prepared.manifestPath).deletingLastPathComponent()
        let failureFile = directory.deletingLastPathComponent().appendingPathComponent("last-error.txt")
        try? FileManager.default.removeItem(at: failureFile)
        let helper = directory.appendingPathComponent("Ka4alkaUpdate-" + UUID().uuidString)
        try FileManager.default.copyItem(at: executable, to: helper)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: helper.path)
        let job = MacUpdateJob(installed: installed.path, application: prepared.application, catalog: prepared.catalogPath, manifest: prepared.manifestPath, signature: prepared.signaturePath, publicKey: try String(contentsOf: keyURL), parent: getpid(), architecture: Self.architecture, reopen: terminate)
        let jobURL = directory.appendingPathComponent("job.json"); try JSONEncoder().encode(job).write(to: jobURL, options: .atomic)
        let process = Process(); process.executableURL = helper; process.arguments = ["--apply-update", jobURL.path]; try process.run()
        installing = true
        UserDefaults.standard.set(failureFile.path, forKey: updateFailurePathKey)
        UserDefaults.standard.removeObject(forKey: "Ka4alkaPreparedUpdate")
        if terminate { requestApplicationQuit() }
    }

}
// Runs from a temporary copy, after the app has saved its queue and exited.
enum MacUpdateInstaller {
    /// True once the installed application was started again after a rolled-back install (so `run` does not start a second copy).
    private(set) static var relaunched = false
    @discardableResult static func apply(_ jobURL: URL, reopen: Bool = true) throws -> Int32? {
        let files = FileManager.default; let job = try JSONDecoder().decode(MacUpdateJob.self, from: Data(contentsOf: jobURL))
        let manifest = try MacManifest.validate(Data(contentsOf: URL(fileURLWithPath: job.manifest)), signature: Data(contentsOf: URL(fileURLWithPath: job.signature)), publicKey: job.publicKey)
        guard let package = manifest.packages[job.architecture], job.installed.hasSuffix("/Ka4alka.app") else { throw UpdateError(tr("Задание на обновление повреждено.", "The update job is damaged.")) }
        let old = URL(fileURLWithPath: job.installed)
        guard files.isWritableFile(atPath: old.deletingLastPathComponent().path), try old.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true else { throw UpdateError(tr("В папку приложения нельзя записывать.", "The application folder is not writable.")) }
        let app = URL(fileURLWithPath: job.application)
        try MacComponentCatalog.validate(Data(contentsOf: URL(fileURLWithPath: job.catalog)), package: package, version: manifest.version, application: app)
        guard let bundle = Bundle(url: app), bundle.bundleIdentifier == "org.ka4alka.app", bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == manifest.version else { throw UpdateError(tr("Скачанное приложение не совпадает с ожидаемым.", "The downloaded application is not the expected one.")) }
        try runTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
        for _ in 0..<300 { if job.parent <= 0 || kill(job.parent, 0) != 0 { break }; Thread.sleep(forTimeInterval: 0.1) }
        guard job.parent <= 0 || kill(job.parent, 0) != 0 else { throw UpdateError(tr("Приложение не завершилось вовремя.", "The application did not exit in time.")) }
        let backup = old.deletingLastPathComponent().appendingPathComponent("Ka4alka-previous-" + UUID().uuidString + ".app")
        try files.moveItem(at: old, to: backup)
        do { try files.moveItem(at: app, to: old) } catch { try? files.moveItem(at: backup, to: old); throw error }
        var launched: Int32?
        do {
            if reopen {
                let health = jobURL.deletingLastPathComponent().appendingPathComponent("health-" + UUID().uuidString)
                let token = UUID().uuidString
                let child = Process(); child.executableURL = old.appendingPathComponent("Contents/MacOS/Ka4alka")
                var environment = ProcessInfo.processInfo.environment
                environment["KACHALKA_UPDATE_HEALTH_PATH"] = health.path; environment["KACHALKA_UPDATE_HEALTH_TOKEN"] = token
                child.environment = environment; try child.run(); launched = child.processIdentifier
                var healthy = false
                for _ in 0..<300 {
                    if let data = try? Data(contentsOf: health), let value = try? JSONDecoder().decode([String: String].self, from: data), value["token"] == token, value["version"] == manifest.version { healthy = true; break }
                    if !child.isRunning { break }; Thread.sleep(forTimeInterval: 0.1)
                }
                try? files.removeItem(at: health)
                if !healthy { if child.isRunning { child.terminate(); child.waitUntilExit() }; throw UpdateError(tr("Новая версия не прошла проверку запуска. Возвращена предыдущая версия.", "The updated application failed its startup check; the previous version was restored.")) }
            }
            try? files.removeItem(at: backup)
            return launched
        } catch {
            try? files.removeItem(at: old); try files.moveItem(at: backup, to: old)
            if reopen { try? runTool("/usr/bin/open", ["-n", old.path]); relaunched = true }
            throw error
        }
    }
    /// Entry point of the helper process. On any failure the installed application is intact (or restored): the reason is written to
    /// `<updates>/last-error.txt` for the next launch, and unless the person was quitting the application is started again.
    static func run(_ path: String) -> Int32 {
        let jobURL = URL(fileURLWithPath: path)
        guard let data = try? Data(contentsOf: jobURL), let job = try? JSONDecoder().decode(MacUpdateJob.self, from: data) else { return 1 }
        let reopen = job.reopen ?? true
        do { try apply(jobURL, reopen: reopen); return 0 }
        catch {
            recordFailure(error.localizedDescription, job: job)
            if reopen, !relaunched { try? runTool("/usr/bin/open", [job.installed]) }
            return 1
        }
    }
    private static func recordFailure(_ text: String, job: MacUpdateJob) {
        let root = URL(fileURLWithPath: job.manifest).deletingLastPathComponent().deletingLastPathComponent()
        guard root.lastPathComponent == "updates" else { return }
        try? text.write(to: root.appendingPathComponent("last-error.txt"), atomically: true, encoding: .utf8)
    }
}
