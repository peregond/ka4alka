import XCTest
import Security
@testable import Ka4alka

final class PlatformTests: XCTestCase {
    func testStableCardDecoding() throws {
        let data = Data(#"{"id":"series:series:yuzhnyi-park","title":"Южный парк","section":"series","year":1997,"rating":"8.2","poster":null,"description":"Описание","genre":"комедия","quality":"Full HD","saved":true,"platform":null}"#.utf8)
        let card = try JSONDecoder().decode(MediaCard.self, from: data)
        XCTAssertEqual(card.id, "series:series:yuzhnyi-park"); XCTAssertEqual(card.quality, "Full HD"); XCTAssertTrue(card.saved)
        XCTAssertEqual(try JSONDecoder().decode(MediaCard.self, from: JSONEncoder().encode(card)), card)
    }
    func testProtocolJSONTypes() throws {
        let value: JSONValue = .object(["id": "fixture", "result": .object(["count": 40, "ready": true])])
        let decoded = try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(value))
        XCTAssertEqual(decoded["id"].string, "fixture"); XCTAssertTrue(decoded["result"]["ready"].bool)
    }
    func testSignedUpdateAndTampering() throws {
        let key = try XCTUnwrap(SecKeyCreateRandomKey([kSecAttrKeyType: kSecAttrKeyTypeRSA, kSecAttrKeySizeInBits: 2048] as CFDictionary, nil))
        let publicKey = try XCTUnwrap(SecKeyCopyPublicKey(key)); let der = try XCTUnwrap(SecKeyCopyExternalRepresentation(publicKey, nil)) as Data
        let pem = "-----BEGIN RSA PUBLIC KEY-----\n" + der.base64EncodedString() + "\n-----END RSA PUBLIC KEY-----"
        let version = "0.44.0"
        let packages = Dictionary(uniqueKeysWithValues: ["arm64", "x64"].map { arch in (arch, MacPackage(url: "https://github.com/peregond/ka4alka/releases/download/v\(version)/Kachalka-macos-\(arch).zip", fileName: "Kachalka-macos-\(arch).zip", size: 4096, sha256: String(repeating: "a", count: 64), components: MacComponentIndex(url: "https://github.com/peregond/ka4alka/releases/download/v\(version)/macos-components-\(arch).json", size: 1024, sha256: String(repeating: "b", count: 64)))) })
        var manifest = MacManifest(schemaVersion: 1, appId: "kachalka", channel: "stable", version: version, platform: "macos", minimumMacOS: "14.0", packages: packages, releaseNotesUrl: "https://github.com/peregond/ka4alka/releases/tag/v" + version)
        func signed(_ value: MacManifest) throws -> (Data, Data) { let bytes = try JSONEncoder().encode(value); return (bytes, try XCTUnwrap(SecKeyCreateSignature(key, .rsaSignatureMessagePKCS1v15SHA256, bytes as CFData, nil)) as Data) }
        let (bytes, signature) = try signed(manifest)
        XCTAssertEqual(try MacManifest.validate(bytes, signature: signature, publicKey: pem).version, version)
        var tampered = bytes; tampered[0] ^= 1
        XCTAssertThrowsError(try MacManifest.validate(tampered, signature: signature, publicKey: pem))
        manifest.packages["arm64"]!.url = "https://attacker.invalid/update.zip"
        let (unsafe, unsafeSignature) = try signed(manifest)
        XCTAssertThrowsError(try MacManifest.validate(unsafe, signature: unsafeSignature, publicKey: pem))
        XCTAssertNil(publicRSAKey("not a public key"))
    }
    func testVersionOrderingAndByteBounds() {
        XCTAssertTrue(MacManifest.newer("0.44.0", than: "0.43.14")); XCTAssertFalse(MacManifest.newer("0.43.9", than: "0.43.14"))
        XCTAssertFalse(byteText(.infinity).isEmpty); XCTAssertFalse(byteText(Double.greatestFiniteMagnitude).isEmpty)
    }
}

final class MacInstallationTests: XCTestCase {
    func testVerifiedApplicationReplacement() throws {
        guard let sourcePath = ProcessInfo.processInfo.environment["KACHALKA_TEST_APP"] else { throw XCTSkip("A signed packaged application is required") }
        let files = FileManager.default; let source = URL(fileURLWithPath: sourcePath)
        let directory = files.temporaryDirectory.appendingPathComponent("kachalka-installer-" + UUID().uuidString)
        try files.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? files.removeItem(at: directory) }
        let installed = directory.appendingPathComponent("installed/Ka4alka.app")
        let candidate = directory.appendingPathComponent("candidate/Ka4alka.app")
        try files.createDirectory(at: installed.deletingLastPathComponent(), withIntermediateDirectories: true)
        try files.createDirectory(at: candidate.deletingLastPathComponent(), withIntermediateDirectories: true)
        try files.copyItem(at: source, to: installed); try files.copyItem(at: source, to: candidate)
        let version = try XCTUnwrap(Bundle(url: candidate)?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String)
        let key = try XCTUnwrap(SecKeyCreateRandomKey([kSecAttrKeyType: kSecAttrKeyTypeRSA, kSecAttrKeySizeInBits: 2048] as CFDictionary, nil))
        let der = try XCTUnwrap(SecKeyCopyExternalRepresentation(try XCTUnwrap(SecKeyCopyPublicKey(key)), nil)) as Data
        let pem = "-----BEGIN RSA PUBLIC KEY-----\n" + der.base64EncodedString() + "\n-----END RSA PUBLIC KEY-----"
        var components: [MacComponent] = []
        for case let file as URL in try XCTUnwrap(files.enumerator(at: candidate, includingPropertiesForKeys: [.isRegularFileKey])) {
            let values = try file.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
            if values.isRegularFile != true { continue }
            let relative = String(file.path.dropFirst(candidate.path.count + 1))
            let attributes = try files.attributesOfItem(atPath: file.path)
            components.append(MacComponent(path: relative, size: Int64(try XCTUnwrap(values.fileSize)), sha256: try sha256File(file), offset: 0, packedSize: 0, method: 8, mode: try XCTUnwrap(attributes[.posixPermissions] as? NSNumber).intValue))
        }
        let index = try JSONEncoder().encode(MacComponentCatalog(schemaVersion: 1, version: version, files: components))
        let catalog = directory.appendingPathComponent("components.json"); try index.write(to: catalog)
        let catalogHash = try sha256File(catalog)
        let packages = Dictionary(uniqueKeysWithValues: ["arm64", "x64"].map { arch in
            (arch, MacPackage(url: "https://github.com/peregond/ka4alka/releases/download/v\(version)/Kachalka-macos-\(arch).zip", fileName: "Kachalka-macos-\(arch).zip", size: 4096, sha256: String(repeating: "a", count: 64), components: MacComponentIndex(url: "https://github.com/peregond/ka4alka/releases/download/v\(version)/macos-components-\(arch).json", size: Int64(index.count), sha256: catalogHash)))
        })
        let manifest = MacManifest(schemaVersion: 1, appId: "kachalka", channel: "stable", version: version, platform: "macos", minimumMacOS: "14.0", packages: packages, releaseNotesUrl: "https://github.com/peregond/ka4alka/releases/tag/v" + version)
        let bytes = try JSONEncoder().encode(manifest)
        let signature = try XCTUnwrap(SecKeyCreateSignature(key, .rsaSignatureMessagePKCS1v15SHA256, bytes as CFData, nil)) as Data
        let manifestFile = directory.appendingPathComponent("manifest.json"); try bytes.write(to: manifestFile)
        let signatureFile = directory.appendingPathComponent("manifest.sig"); try signature.write(to: signatureFile)
        let job = MacUpdateJob(installed: installed.path, application: candidate.path, catalog: catalog.path, manifest: manifestFile.path, signature: signatureFile.path, publicKey: pem, parent: 0, architecture: "arm64")
        let jobFile = directory.appendingPathComponent("job.json"); try JSONEncoder().encode(job).write(to: jobFile)
        // A corrupted candidate must never replace the installed application.
        let artwork = candidate.appendingPathComponent("Contents/Resources/en.json")
        let original = try Data(contentsOf: artwork); try Data("tampered".utf8).write(to: artwork)
        XCTAssertThrowsError(try MacUpdateInstaller.apply(jobFile, reopen: false))
        XCTAssertTrue(files.fileExists(atPath: installed.appendingPathComponent("Contents/MacOS/Ka4alka").path))
        try original.write(to: artwork)
        try MacUpdateInstaller.apply(jobFile, reopen: false)
        XCTAssertFalse(files.fileExists(atPath: candidate.path))
        XCTAssertEqual(Bundle(url: installed)?.bundleIdentifier, "org.ka4alka.app")
        XCTAssertFalse(try files.contentsOfDirectory(atPath: installed.deletingLastPathComponent().path).contains(where: { $0.hasPrefix("Ka4alka-previous") }))
        try runTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", installed.path])
    }
}
