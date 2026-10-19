import AppKit
import Darwin
import XCTest
import Security
@testable import Ka4alka

final class PlatformTests: XCTestCase {
    func testCanonicalComponentBoundary() throws {
        let files = FileManager.default
        let root = files.temporaryDirectory.appendingPathComponent("качалка-path-" + UUID().uuidString)
        try files.createDirectory(at: root.appendingPathComponent("Contents"), withIntermediateDirectories: true)
        defer { try? files.removeItem(at: root) }
        let file = root.appendingPathComponent("Contents/test.json")
        try Data().write(to: file)
        XCTAssertEqual(try MacComponentCatalog.relativePath(file.resolvingSymlinksInPath(), in: root), "Contents/test.json")
        XCTAssertThrowsError(try MacComponentCatalog.relativePath(root.deletingLastPathComponent().appendingPathComponent("outside.json"), in: root))
    }
    func testLiteralComponentPaths() {
        XCTAssertTrue(MacComponentCatalog.safePath("Contents/MacOS/Ka4alka"))
        XCTAssertTrue(MacComponentCatalog.safePath("Contents/Resources/en.json"))
        for path in ["Contents/../escape", "Contents//bad", "/Contents/bad", "Contents/x\0y", "Contents/x\ny", "Contents/x\\y", "Contents/x:y"] {
            XCTAssertFalse(MacComponentCatalog.safePath(path), path.debugDescription)
        }
    }
    func testStableCardDecoding() throws {
        let data = Data(#"{"id":"series:series:yuzhnyi-park","title":"Южный парк","section":"series","year":1997,"rating":"8.2","poster":null,"description":"Описание","genre":"комедия","quality":"Full HD","saved":true,"platform":null}"#.utf8)
        let card = try JSONDecoder().decode(MediaCard.self, from: data)
        XCTAssertEqual(card.id, "series:series:yuzhnyi-park"); XCTAssertEqual(card.quality, "Full HD"); XCTAssertTrue(card.saved)
        XCTAssertEqual(try JSONDecoder().decode(MediaCard.self, from: JSONEncoder().encode(card)), card)
    }
    func testChunkedPipeMessages() throws {
        final class Received: @unchecked Sendable { var lines: [Data] = []; var errors: [String] = [] }
        let received = Received()
        let decoder = PipeMessageDecoder(limit: 128, onMessage: { received.lines.append($0) }, onError: { received.errors.append($0) })
        let text = #"{"title":"Южный парк"}"#
        let bytes = Data((text + "\n" + text + "\n").utf8)
        for byte in bytes { XCTAssertTrue(decoder.consume(Data([byte]))) }
        XCTAssertEqual(received.lines.count, 2)
        XCTAssertEqual(String(data: received.lines[0], encoding: .utf8), text)
        XCTAssertFalse(decoder.consume(Data(repeating: 65, count: 129)))
        XCTAssertEqual(received.errors.count, 1)
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

// MARK: Decoding of the engine contract

final class ModelDecodingTests: XCTestCase {
    func testTransferWithNullTotalBytesAndNoStatusKind() throws {
        let json = #"{"id":"t1","title":"Фильм","releaseTitle":"Film.2026","progress":12.5,"status":"Качается","hint":"","paused":false,"complete":false,"missing":false,"lowSpace":false,"downloadRate":1000,"uploadRate":0,"totalBytes":null,"addedUtc":"2026-10-01T10:00:00Z","folder":"/tmp","mediaId":null,"canUpdateRelease":false,"peers":"3","files":1}"#
        let item = try TestFixtures.decode(Transfer.self, json)
        XCTAssertNil(item.totalBytes)
        XCTAssertNil(item.statusKind)
        XCTAssertNil(item.remaining)
        XCTAssertNil(item.indeterminate)
        XCTAssertTrue(item.isActive)
    }
    func testTransferNeedsOnlyAnIdentifier() throws {
        let item = try TestFixtures.decode(Transfer.self, #"{"id":"only"}"#)
        XCTAssertEqual(item.id, "only")
        XCTAssertEqual(item.title, "")
        XCTAssertEqual(item.progress, 0)
        XCTAssertFalse(item.paused)
        XCTAssertNil(item.totalBytes)
    }
    func testTransferWrongTypesFallBackToDefaults() throws {
        let item = try TestFixtures.decode(Transfer.self, #"{"id":"x","progress":"oops","paused":"no","files":"many","totalBytes":"big"}"#)
        XCTAssertEqual(item.progress, 0)
        XCTAssertFalse(item.paused)
        XCTAssertEqual(item.files, 0)
        XCTAssertNil(item.totalBytes)
    }
    func testQueueSurvivesOneBrokenItem() throws {
        let result = try TestFixtures.decode(DownloadResult.self, #"{"items":[{"id":"a"},{"id":"b","progress":"oops","statusKind":"error"},{"id":"c","totalBytes":null}]}"#)
        XCTAssertEqual(result.items.map { $0.id }, ["a", "b", "c"])
        XCTAssertFalse(result.items[1].isActive)
        XCTAssertTrue(result.items[2].isActive)
    }
    func testMediaCardWithBothRatings() throws {
        let card = try TestFixtures.decode(MediaCard.self, TestFixtures.cardJSON(id: "movies:a", title: "A", extra: #","kp":"8.1","imdb":"7.9","ratingSource":"Кинопоиск""#))
        XCTAssertEqual(card.ratings.count, 2)
        XCTAssertEqual(card.ratings[0].source, KDRatingSource.kinopoisk)
        XCTAssertEqual(card.ratings[0].value, "8.1")
        XCTAssertEqual(card.ratings[1].source, KDRatingSource.imdb)
        XCTAssertEqual(card.ratings[1].value, "7.9")
    }
    func testMediaCardWithoutRatingFieldsStillDecodes() throws {
        let card = try TestFixtures.card("movies:b", "B")
        XCTAssertNil(card.kp); XCTAssertNil(card.imdb); XCTAssertNil(card.ratingSource)
        XCTAssertEqual(card.ratings.count, 1)
        XCTAssertEqual(card.ratings[0].value, "7.8")
    }
    func testSingleRatingKeepsItsSource() throws {
        let card = try TestFixtures.decode(MediaCard.self, TestFixtures.cardJSON(id: "movies:c", title: "C", extra: #","ratingSource":"IMDb""#))
        XCTAssertEqual(card.ratings.count, 1)
        XCTAssertEqual(card.ratings[0].source, KDRatingSource.imdb)
    }
    func testUnusableRatingsAreHidden() throws {
        let card = try TestFixtures.decode(MediaCard.self, #"{"id":"movies:d","title":"D","section":"movies","year":2024,"rating":"—","poster":null,"description":"","genre":"","quality":null,"saved":false,"platform":null,"kp":"","imdb":"—"}"#)
        XCTAssertTrue(card.ratings.isEmpty)
    }
    func testStoreCardWithSoonAndReviewTone() throws {
        let card = try TestFixtures.decode(MediaCard.self, TestFixtures.cardJSON(id: "games:a", title: "G", section: "games", extra: #","store":{"price":"","originalPrice":"","discount":0,"free":false,"reviewPercent":0,"soon":true,"soonLabel":"Скоро","released":"2027","reviewTone":"mixed","reviewLabel":"Смешанные","reviewCount":120}"#))
        XCTAssertEqual(card.store?.soon, true)
        XCTAssertEqual(card.store?.reviewTone, "mixed")
        XCTAssertEqual(card.store?.reviewCount, 120)
        let plain = try TestFixtures.decode(MediaCard.self, TestFixtures.cardJSON(id: "games:b", title: "G2", section: "games", extra: #","store":{"price":"1","originalPrice":"","discount":0,"free":false,"reviewPercent":50}"#))
        XCTAssertNil(plain.store?.soon)
        XCTAssertNil(plain.store?.reviewTone)
    }
    func testReleaseRowWithoutRussianDub() throws {
        let plain = try TestFixtures.decode(ReleaseRow.self, TestFixtures.releaseJSON(id: "r1", title: "T"))
        XCTAssertNil(plain.russianDub)
        XCTAssertNil(plain.qualityLabel)
        let marked = try TestFixtures.decode(ReleaseRow.self, TestFixtures.releaseJSON(id: "r2", title: "T", extra: #","russianDub":true,"qualityLabel":"Full HD""#))
        XCTAssertEqual(marked.russianDub, true)
        XCTAssertEqual(marked.qualityLabel, "Full HD")
    }
    func testDetailAndSettingsExtrasAreOptional() throws {
        let card: String = TestFixtures.cardJSON(id: "movies:e", title: "E")
        let bare = try TestFixtures.decode(DetailResult.self, "{\"item\":" + card + ",\"people\":[],\"awards\":[]}")
        XCTAssertNil(bare.originalTitle); XCTAssertNil(bare.resolutions)
        let full = try TestFixtures.decode(DetailResult.self, "{\"item\":" + card + ",\"people\":[],\"awards\":[],\"originalTitle\":\"E\",\"genres\":\"драма\",\"country\":\"США\",\"resolutions\":[2160,1080]}")
        XCTAssertEqual(full.resolutions ?? [], [2160, 1080])
        let settings = try TestFixtures.decode(AppSettings.self, TestFixtures.settingsJSON)
        XCTAssertNil(settings.homeCountry)
        let withCountry = TestFixtures.settingsJSON.replacingOccurrences(of: "\"savedIds\":[]", with: "\"savedIds\":[],\"homeCountry\":\"rossiia\"")
        XCTAssertEqual(try TestFixtures.decode(AppSettings.self, withCountry).homeCountry, "rossiia")
    }
    func testSourceStateCount() throws {
        let sources = try TestFixtures.decode(ReleaseResult.self, #"{"items":[],"complete":true,"sources":[{"name":"A","state":"Ready","count":12},{"name":"B","state":"Empty"}]}"#)
        XCTAssertEqual(sources.sources[0].count, 12)
        XCTAssertNil(sources.sources[1].count)
    }
    func testDecoderResynchronisesAfterAnOversizedMessage() {
        final class Received: @unchecked Sendable { var lines: [Data] = []; var errors: [String] = [] }
        let received = Received()
        let decoder = PipeMessageDecoder(limit: 32, onMessage: { received.lines.append($0) }, onError: { received.errors.append($0) })
        XCTAssertFalse(decoder.consume(Data(repeating: 65, count: 40)))
        XCTAssertEqual(received.errors.count, 1)
        XCTAssertTrue(decoder.consume(Data("rest of the big message\n".utf8)))
        XCTAssertTrue(decoder.consume(Data("{\"ok\":1}\n".utf8)))
        XCTAssertEqual(received.lines.count, 1)
        XCTAssertEqual(String(data: received.lines[0], encoding: .utf8), "{\"ok\":1}")
        XCTAssertFalse(decoder.consume(Data(repeating: 66, count: 40) + Data("\n".utf8)))
        XCTAssertTrue(decoder.consume(Data("{\"ok\":2}\n".utf8)))
        XCTAssertEqual(received.lines.count, 2)
        XCTAssertEqual(received.errors.count, 2)
    }
}

// MARK: State machine of the model (no engine)

@MainActor final class ModelLogicTests: XCTestCase {
    private func makeModel() -> AppModel {
        let model = AppModel()
        model.bridge.disabled = true
        return model
    }

    func testSelectClearsContextOnSectionChange() throws {
        let model = makeModel()
        model.select(.movies)
        model.filter.genre = "drama"
        model.page = 3
        model.submittedQuery = "матрица"
        model.query = "матрица"
        model.select(.series)
        XCTAssertEqual(model.section, Section.series)
        XCTAssertEqual(model.filter, CatalogFilter())
        XCTAssertEqual(model.page, 1)
        XCTAssertEqual(model.submittedQuery, "")
        XCTAssertEqual(model.query, "")
        XCTAssertNil(model.list)
    }
    func testSelectKeepsContextWhenReturningFromADetailPage() throws {
        let model = makeModel()
        model.select(.movies)
        model.filter.genre = "drama"
        model.submittedQuery = "abc"
        model.open(try TestFixtures.card("movies:one", "One"))
        XCTAssertEqual(model.routes.count, 2)
        let revision = model.revision
        model.select(.movies)
        XCTAssertEqual(model.routes.count, 1)
        XCTAssertEqual(model.filter.genre, "drama")
        XCTAssertEqual(model.submittedQuery, "abc")
        XCTAssertEqual(model.revision, revision)
    }
    func testSelectOnTheSectionRootResetsFilters() {
        let model = makeModel()
        model.select(.movies)
        model.filter.genre = "drama"
        model.submittedQuery = "abc"
        let revision = model.revision
        model.select(.movies)
        XCTAssertEqual(model.filter, CatalogFilter())
        XCTAssertEqual(model.submittedQuery, "")
        XCTAssertEqual(model.revision, revision + 1)
    }
    func testChangingTheFilterResetsThePage() {
        let model = makeModel()
        model.page = 4
        model.filter.quality = "4K"
        XCTAssertEqual(model.page, 1)
        model.page = 3
        model.filter.quality = "4K"
        XCTAssertEqual(model.page, 3, "assigning the same filter must not reset the page")
    }
    func testVisibleHidesOnlyHiddenCards() throws {
        let model = makeModel()
        let hidden = try TestFixtures.card("movies:hidden", "Hidden")
        let poor = try TestFixtures.decode(MediaCard.self, #"{"id":"movies:poor","title":"Poor","section":"movies","year":2020,"rating":"5.0","poster":null,"description":"","genre":"","quality":"CAM","saved":false,"platform":null}"#)
        let fine = try TestFixtures.card("movies:fine", "Fine")
        model.hiddenCards = ["movies:hidden"]
        XCTAssertEqual(model.visible([hidden, poor, fine]).map { $0.id }, ["movies:poor", "movies:fine"])
    }
    func testReleasesBelongToTheirCard() throws {
        let model = makeModel()
        let first = try TestFixtures.card("movies:one", "One")
        let second = try TestFixtures.card("movies:two", "Two")
        let row = try TestFixtures.decode(ReleaseRow.self, TestFixtures.releaseJSON(id: "r1", title: "T"))
        model.releasesOwner = first.id
        model.releases = [row]
        XCTAssertEqual(model.releases(for: first).count, 1)
        XCTAssertTrue(model.releases(for: second).isEmpty)
        XCTAssertTrue(model.sources(for: second).isEmpty)
    }
    func testOpeningACardClearsTheReleasesOfThePreviousOne() throws {
        let model = makeModel()
        let first = try TestFixtures.card("movies:one", "One")
        model.releasesOwner = first.id
        model.releases = [try TestFixtures.decode(ReleaseRow.self, TestFixtures.releaseJSON(id: "r1", title: "T"))]
        model.open(try TestFixtures.card("movies:two", "Two"))
        XCTAssertTrue(model.releases.isEmpty)
        XCTAssertNil(model.releasesOwner)
    }
    func testTransferCountsExcludePausedFinishedAndFailed() throws {
        let store = TransferStore()
        defer { store.items = [] }
        let queue = #"[{"id":"a","statusKind":"downloading","downloadRate":1000,"uploadRate":50},{"id":"b","paused":true,"downloadRate":-5},{"id":"c","complete":true,"uploadRate":200},{"id":"d","statusKind":"error"},{"id":"e","totalBytes":null,"downloadRate":500}]"#
        store.items = try TestFixtures.decode([Transfer].self, queue)
        XCTAssertEqual(store.activeCount, 2)
        XCTAssertEqual(store.downloadRate, 1500)
        XCTAssertEqual(store.uploadRate, 250)
    }
    func testFailIgnoresCancellationAndTheDisabledEngine() {
        let model = makeModel()
        model.fail(CancellationError())
        XCTAssertNil(model.error)
        model.fail(RPCError(code: "disabled", message: "off"))
        XCTAssertNil(model.error)
        model.fail(RPCError(code: "boom", message: "Broken"))
        XCTAssertEqual(model.error, "Broken")
    }
    func testBackNeverRemovesTheRootRoute() throws {
        let model = makeModel()
        model.back()
        XCTAssertEqual(model.routes.count, 1)
        model.open(try TestFixtures.card("movies:one", "One"))
        model.back()
        XCTAssertEqual(model.route, Route.section(.featured))
    }
}

// MARK: Formatting

final class FormattingTests: XCTestCase {
    func testBytesNeverSayZeroKB() {
        let saved = TestLanguage.current()
        defer { TestLanguage.restore(saved) }
        for language in ["ru", "en"] {
            UserDefaults.standard.set(language, forKey: "UiLanguage")
            XCTAssertFalse(KDFormat.bytes(0).lowercased().contains("zero"))
            XCTAssertTrue(KDFormat.bytes(0).hasPrefix("0 "), KDFormat.bytes(0))
            XCTAssertFalse(KDFormat.bytes(.infinity).isEmpty)
            XCTAssertFalse(KDFormat.bytes(-1).isEmpty)
        }
    }
    func testBytesFollowTheInterfaceLanguage() {
        let saved = TestLanguage.current()
        defer { TestLanguage.restore(saved) }
        UserDefaults.standard.set("ru", forKey: "UiLanguage")
        XCTAssertEqual(KDFormat.bytes(1_400_000_000), "1,4 ГБ")
        XCTAssertEqual(KDFormat.bytes(512), "512 Б")
        UserDefaults.standard.set("en", forKey: "UiLanguage")
        XCTAssertEqual(KDFormat.bytes(1_400_000_000), "1.4 GB")
        XCTAssertEqual(KDFormat.bytes(512), "512 B")
    }
    func testPercentFloorsInsteadOfRounding() {
        let saved = TestLanguage.current()
        defer { TestLanguage.restore(saved) }
        UserDefaults.standard.set("en", forKey: "UiLanguage")
        XCTAssertEqual(KDFormat.percent(99.96), "99%")
        XCTAssertEqual(KDFormat.percent(100), "100%")
        XCTAssertEqual(KDFormat.percent(-3), "0%")
        XCTAssertEqual(KDFormat.percent(.nan), "0%")
        XCTAssertEqual(KDFormat.percent(42.78, fractionDigits: 1), "42.7%")
    }
    func testMastheadDateStartsWithACapital() {
        let saved = TestLanguage.current()
        defer { TestLanguage.restore(saved) }
        let noon = Date(timeIntervalSince1970: 1_792_000_000)
        for language in ["ru", "en"] {
            UserDefaults.standard.set(language, forKey: "UiLanguage")
            let text = KDFormat.mastheadDate(noon)
            XCTAssertFalse(text.isEmpty)
            if let first = text.first { XCTAssertTrue(first.isUppercase, text) }
        }
    }
}

// MARK: Localisation lint (reads the sources)

final class LocalizationLintTests: XCTestCase {
    /// Russian-only words that are chosen by hand next to an English branch (plural forms, units, matching keys).
    private let allowedLiterals: Set<String> = [
        "Русский", "Не указано", "КП", "кп", "кино", "Б", "КБ", "МБ", "ГБ", "ТБ", "Актёры", "Режиссёры", "Операторы", "-е",
        "название", "названия", "названий", "загрузка", "загрузки", "загрузок", "Пауза", "Продолжить", "[А-Яа-яЁё]"
    ]
    private func literalExpression() throws -> NSRegularExpression {
        return try NSRegularExpression(pattern: "\"((?:[^\"\\\\\\n]|\\\\.)*)\"")
    }
    func testNoCyrillicLiteralOutsideTr() throws {
        let literal = try literalExpression()
        var offenders: [String] = []
        for file in try TestSources.swiftFiles() {
            var number = 0
            for line in file.text.components(separatedBy: "\n") {
                number += 1
                if line.trimmingCharacters(in: .whitespaces).hasPrefix("//") { continue }
                let ns = line as NSString
                for match in literal.matches(in: line, range: NSRange(location: 0, length: ns.length)) {
                    let text = ns.substring(with: match.range(at: 1))
                    if !SmokeText.hasCyrillic(text) || allowedLiterals.contains(text) { continue }
                    let before = ns.substring(to: match.range.location).trimmingCharacters(in: .whitespaces)
                    if before.hasSuffix("tr(") { continue }
                    offenders.append(file.name + ":" + String(number) + " " + text)
                }
            }
        }
        XCTAssertTrue(offenders.isEmpty, "Russian text that bypasses tr(\"ru\", \"en\"):\n" + offenders.joined(separator: "\n"))
    }
    func testTwoArgumentTrHasARealEnglishText() throws {
        let literal = "\"((?:[^\"\\\\\\n]|\\\\.)*)\""
        let expression = try NSRegularExpression(pattern: "\\btr\\(\\s*" + literal + "\\s*,\\s*" + literal + "\\s*\\)")
        var offenders: [String] = []
        for file in try TestSources.swiftFiles() {
            let ns = file.text as NSString
            for match in expression.matches(in: file.text, range: NSRange(location: 0, length: ns.length)) {
                let russian = ns.substring(with: match.range(at: 1))
                let english = ns.substring(with: match.range(at: 2))
                if english.trimmingCharacters(in: .whitespaces).isEmpty { offenders.append(file.name + ": empty English text for " + russian) }
                if SmokeText.hasCyrillic(english) { offenders.append(file.name + ": Cyrillic English text " + english) }
                if !SmokeText.hasCyrillic(russian) { offenders.append(file.name + ": the Russian text has no Cyrillic: " + russian) }
            }
        }
        XCTAssertTrue(offenders.isEmpty, offenders.joined(separator: "\n"))
    }
    func testOneArgumentTrIsInTheEnglishDictionary() throws {
        let data = try Data(contentsOf: TestSources.repository.appendingPathComponent("native/Localization/en.json"))
        let dictionary = try JSONDecoder().decode([String: String].self, from: data)
        let expression = try NSRegularExpression(pattern: "\\btr\\(\\s*\"((?:[^\"\\\\\\n]|\\\\.)*)\"\\s*\\)")
        var missing: [String] = []
        for file in try TestSources.swiftFiles() {
            let ns = file.text as NSString
            for match in expression.matches(in: file.text, range: NSRange(location: 0, length: ns.length)) {
                let russian = ns.substring(with: match.range(at: 1))
                if SmokeText.hasCyrillic(russian) && dictionary[russian] == nil { missing.append(file.name + ": " + russian) }
            }
        }
        XCTAssertTrue(missing.isEmpty, "tr() with one argument needs an entry in native/Localization/en.json:\n" + missing.joined(separator: "\n"))
    }
}

final class MacInstallationTests: XCTestCase {
    func testVerifiedApplicationReplacement() throws {
        guard let sourcePath = ProcessInfo.processInfo.environment["KACHALKA_TEST_APP"] else { throw XCTSkip("A signed packaged application is required") }
        let files = FileManager.default; let source = URL(fileURLWithPath: sourcePath)
        let directory = files.temporaryDirectory.resolvingSymlinksInPath().appendingPathComponent("kachalka-installer-" + UUID().uuidString)
        try files.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? files.removeItem(at: directory) }
        let installed = directory.appendingPathComponent("installed/Ka4alka.app")
        let candidate = directory.appendingPathComponent("candidate/Ka4alka.app")
        try files.createDirectory(at: installed.deletingLastPathComponent(), withIntermediateDirectories: true)
        try files.createDirectory(at: candidate.deletingLastPathComponent(), withIntermediateDirectories: true)
        try files.copyItem(at: source, to: installed); try files.copyItem(at: source, to: candidate)
        let jobFile = try makeJob(installed: installed, candidate: candidate, directory: directory)
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
    private func makeJob(installed: URL, candidate: URL, directory: URL) throws -> URL {
        let files = FileManager.default
        let version = try XCTUnwrap(Bundle(url: candidate)?.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String)
        let key = try XCTUnwrap(SecKeyCreateRandomKey([kSecAttrKeyType: kSecAttrKeyTypeRSA, kSecAttrKeySizeInBits: 2048] as CFDictionary, nil))
        let der = try XCTUnwrap(SecKeyCopyExternalRepresentation(try XCTUnwrap(SecKeyCopyPublicKey(key)), nil)) as Data
        let pem = "-----BEGIN RSA PUBLIC KEY-----\n" + der.base64EncodedString() + "\n-----END RSA PUBLIC KEY-----"
        var components: [MacComponent] = []
        for case let file as URL in try XCTUnwrap(files.enumerator(at: candidate, includingPropertiesForKeys: [.isRegularFileKey])) {
            let values = try file.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
            if values.isRegularFile != true { continue }
            let relative = try MacComponentCatalog.relativePath(file, in: candidate)
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
        let job = MacUpdateJob(installed: installed.path, application: candidate.path, catalog: catalog.path, manifest: manifestFile.path, signature: signatureFile.path, publicKey: pem, parent: 0, architecture: MacUpdater.architecture)
        let jobFile = directory.appendingPathComponent("job.json"); try JSONEncoder().encode(job).write(to: jobFile)
        return jobFile
    }
    func testSuccessfulStartupAndRollback() throws {
        guard let sourcePath = ProcessInfo.processInfo.environment["KACHALKA_TEST_APP"] else { throw XCTSkip("A signed packaged application is required") }
        let files = FileManager.default
        let directory = files.temporaryDirectory.resolvingSymlinksInPath().appendingPathComponent("kachalka-launch-check-" + UUID().uuidString)
        try files.createDirectory(at: directory, withIntermediateDirectories: true)
        let installed = directory.appendingPathComponent("installed/Ka4alka.app")
        let candidate = directory.appendingPathComponent("candidate/Ka4alka.app")
        let source = URL(fileURLWithPath: sourcePath)
        try files.createDirectory(at: installed.deletingLastPathComponent(), withIntermediateDirectories: true)
        try files.createDirectory(at: candidate.deletingLastPathComponent(), withIntermediateDirectories: true)
        try files.copyItem(at: source, to: installed); try files.copyItem(at: source, to: candidate)
        let profile = directory.appendingPathComponent("profile")
        try files.createDirectory(at: profile, withIntermediateDirectories: true)
        let settings: [String: JSONValue] = ["Folder": .string(profile.path), "FolderConfigured": true, "CheckForUpdates": false, "AutoUpdate": false, "AutoResumeDownloads": false]
        try JSONEncoder().encode(settings).write(to: profile.appendingPathComponent("settings.json"))
        let previous = ProcessInfo.processInfo.environment["KACHALKA_DATA"]
        setenv("KACHALKA_DATA", profile.path, 1)
        defer {
            if let previous { setenv("KACHALKA_DATA", previous, 1) } else { unsetenv("KACHALKA_DATA") }
            // Only test-owned copies, never a user's installed application.
            RunLoop.current.run(until: Date().addingTimeInterval(0.5))
            for _ in 0..<30 {
                let owned = NSRunningApplication.runningApplications(withBundleIdentifier: "org.ka4alka.app").filter { $0.bundleURL?.resolvingSymlinksInPath().standardizedFileURL == installed.resolvingSymlinksInPath().standardizedFileURL }
                for app in owned { app.forceTerminate() }
                if owned.isEmpty { break }
                RunLoop.current.run(until: Date().addingTimeInterval(0.1))
            }
            try? files.removeItem(at: directory)
        }
        let healthy = try makeJob(installed: installed, candidate: candidate, directory: directory)
        let child = try XCTUnwrap(MacUpdateInstaller.apply(healthy))
        XCTAssertTrue(files.fileExists(atPath: installed.appendingPathComponent("Contents/MacOS/Ka4alka").path))
        kill(child, SIGTERM)
        for _ in 0..<30 { if kill(child, 0) != 0 { break }; Thread.sleep(forTimeInterval: 0.1) }
        try files.copyItem(at: source, to: candidate)
        let engine = "Contents/Resources/engine/Kachalka.Engine.dll"
        let originalHash = try sha256File(installed.appendingPathComponent(engine))
        // The publisher signature and Apple code signature are valid, but the
        // deliberately invalid shared engine cannot complete a real bootstrap.
        try Data("invalid engine fixture".utf8).write(to: candidate.appendingPathComponent(engine))
        try runTool("/usr/bin/codesign", ["--force", "--sign", "-", candidate.path])
        for case let file as URL in try XCTUnwrap(files.enumerator(at: candidate, includingPropertiesForKeys: [.isRegularFileKey])) {
            if try file.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile == true {
                try files.setAttributes([.posixPermissions: files.isExecutableFile(atPath: file.path) ? 0o755 : 0o644], ofItemAtPath: file.path)
            }
        }
        let broken = try makeJob(installed: installed, candidate: candidate, directory: directory)
        XCTAssertThrowsError(try MacUpdateInstaller.apply(broken))
        XCTAssertEqual(try sha256File(installed.appendingPathComponent(engine)), originalHash)
        try runTool("/usr/bin/codesign", ["--verify", "--deep", "--strict", installed.path])
    }

}
