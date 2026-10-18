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
