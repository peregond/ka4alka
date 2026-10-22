import Foundation

enum JSONValue: Codable, Sendable {
    case object([String: JSONValue]), array([JSONValue]), string(String), number(Double), bool(Bool), null
    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() { self = .null }
        else if let v = try? c.decode(Bool.self) { self = .bool(v) }
        else if let v = try? c.decode(Double.self) { self = .number(v) }
        else if let v = try? c.decode(String.self) { self = .string(v) }
        else if let v = try? c.decode([JSONValue].self) { self = .array(v) }
        else { self = .object(try c.decode([String: JSONValue].self)) }
    }
    func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self { case .object(let v): try c.encode(v); case .array(let v): try c.encode(v); case .string(let v): try c.encode(v); case .number(let v): try c.encode(v); case .bool(let v): try c.encode(v); case .null: try c.encodeNil() }
    }
    subscript(_ key: String) -> JSONValue { if case .object(let v) = self { return v[key] ?? .null }; return .null }
    var string: String { if case .string(let v) = self { return v }; return "" }
    var number: Double { if case .number(let v) = self { return v }; return 0 }
    var bool: Bool { if case .bool(let v) = self { return v }; return false }
    var array: [JSONValue] { if case .array(let v) = self { return v }; return [] }
    func decode<T: Decodable>(_ type: T.Type = T.self) throws -> T { try JSONDecoder().decode(type, from: JSONEncoder().encode(self)) }
}
extension JSONValue: ExpressibleByStringLiteral { init(stringLiteral value: String) { self = .string(value) } }
extension JSONValue: ExpressibleByIntegerLiteral { init(integerLiteral value: Int) { self = .number(Double(value)) } }
extension JSONValue: ExpressibleByBooleanLiteral { init(booleanLiteral value: Bool) { self = .bool(value) } }

struct StoreCard: Codable, Equatable {
    var price: String; var originalPrice: String; var discount: Int; var free: Bool; var reviewPercent: Int
    var soon: Bool? = nil; var soonLabel: String? = nil; var released: String? = nil
    var reviewTone: String? = nil; var reviewLabel: String? = nil; var reviewCount: Int? = nil
}
struct MediaCard: Codable, Identifiable, Equatable {
    var id: String; var title: String; var section: String; var year: Int; var rating: String; var poster: String?; var description: String; var genre: String; var quality: String?; var saved: Bool; var platform: String?; var store: StoreCard? = nil
    var kp: String? = nil; var imdb: String? = nil; var ratingSource: String? = nil
    /// Kinopoisk then IMDb when the engine sent both; otherwise the single card rating with its source (never a guessed one).
    var ratings: [KDRating] {
        var rows: [KDRating] = []
        if let kp {
            let row = KDRating(source: .kinopoisk, value: kp)
            if row.isUsable { rows.append(row) }
        }
        if let imdb {
            let row = KDRating(source: .imdb, value: imdb)
            if row.isUsable { rows.append(row) }
        }
        if rows.isEmpty {
            let row = KDRating(source: KDRatingSource(name: ratingSource), value: rating)
            if row.isUsable { rows.append(row) }
        }
        return rows
    }
}
struct Choice: Codable, Identifiable { var key: String; var label: String; var id: String { key } }
struct HiddenTitle: Codable, Identifiable { var id: String; var title: String }
struct AppSettings: Codable {
    var folder: String; var folderConfigured: Bool; var uiLanguage: String; var light: Bool; var liteMode: Bool
    var autoResumeDownloads: Bool; var autoRecoverDownloads: Bool; var notifyDownloads: Bool; var checkForUpdates: Bool; var autoUpdate: Bool
    var hidePoorQuality: Bool; var minimumReleaseHeight: Int; var maxDownloadKbps: Int; var maxUploadKbps: Int; var lanEnabled: Bool; var downloadSort: String
    var hidden: [HiddenTitle]; var hiddenGenres: [String]; var savedIds: [String]
    var homeCountry: String? = nil
}
struct Bootstrap: Decodable { var version: String; var settings: AppSettings; var genres: [Choice]; var countries: [Choice]; var gameGenres: [Choice]; var engineCreated: Bool }
struct CatalogResult: Decodable { var items: [MediaCard]; var hasNext: Bool; var offline: Bool?; var title: String?; var description: String? }
struct CuratedGroup: Decodable, Identifiable { var id: String; var title: String; var description: String; var layout: String; var items: [MediaCard]; var count: Int }
struct FeaturedResult: Decodable { var groups: [CuratedGroup] }
struct Person: Codable, Identifiable { var name: String; var role: String; var pageUrl: String; var profileUrl: String?; var photoUrl: String?; var originalName: String?; var sourcePersonId: String?; var id: String { pageUrl.isEmpty ? name + role : pageUrl } }
struct CinemaAward: Decodable, Identifiable { var name: String; var year: Int; var category: String; var winner: Bool; var personUrl: String?; var id: String { "\(name)-\(year)-\(category)" } }
struct GameShot: Decodable, Identifiable { var thumb: String; var full: String; var id: String { full } }
struct GameDetail: Decodable { var requirements: String; var developers: String; var publishers: String; var price: String; var metacritic: Int; var gallery: [GameShot]; var traits: [String]; var platform: String; var originalPrice: String; var discount: Int; var releaseDate: String; var age: Int; var russian: String; var steamUrl: String?; var steamDbUrl: String?; var review: GameReviews? }
struct GameReviews: Decodable { var percent: Int; var count: Int; var score: Int }
struct DetailResult: Decodable {
    var item: MediaCard; var backdrop: String?; var people: [Person]; var awards: [CinemaAward]; var game: GameDetail?
    var originalTitle: String? = nil; var genres: String? = nil; var country: String? = nil; var resolutions: [Int]? = nil
}
struct FranchiseResult: Decodable { var name: String; var items: [MediaCard] }
struct PersonResult: Decodable { var person: Person; var description: String; var items: [MediaCard]; var sourceUrl: String? }
struct ScopedReleaseRecommendation: Equatable { var owner: String; var id: String? }
struct ReleaseRow: Decodable, Identifiable, Equatable { var id: String; var title: String; var source: String; var size: Double?; var seeds: Int?; var leechers: Int?; var quality: String; var type: String; var audio: String; var studios: [String]; var audioTypes: [String]; var codec: String; var subs: String; var hdr: String; var poor: Bool; var downloadable: Bool; var recommended: Bool; var season: Int?; var episode: Int?; var multipleSeasons: Bool; var multipleEpisodes: Bool; var englishAudio: Bool; var russianAudio: Bool; var gameKind: String?; var gameGroup: String?; var gameVersion: String?
    var russianDub: Bool? = nil; var qualityLabel: String? = nil
    var recommendationOrder: Int? = nil
    /// The tracker's own id of the release (matches `Transfer.releaseId` of a queued download); nil from an older engine.
    var entryId: String? = nil
}
struct SourceState: Decodable, Identifiable { var name: String; var state: String; var count: Int? = nil; var id: String { name } }
struct ReleaseResult: Decodable { var request: String?; var items: [ReleaseRow]; var complete: Bool; var sources: [SourceState] }
struct Transfer: Decodable, Identifiable, Equatable {
    var id: String; var title: String; var releaseTitle: String; var poster: String?; var progress: Double; var status: String; var hint: String; var paused: Bool; var complete: Bool; var missing: Bool; var lowSpace: Bool; var downloadRate: Double; var uploadRate: Double
    /// Nil until the torrent metadata is known.
    var totalBytes: Double?
    var addedUtc: String; var folder: String; var mediaId: String?; var canUpdateRelease: Bool; var peers: String; var files: Int
    /// downloading | waiting | seeding | paused | checking | error (nil from an older engine).
    var statusKind: String? = nil
    /// ETA text such as "осталось 5 мин"; empty when unknown.
    var remaining: String? = nil
    var indeterminate: Bool? = nil
    /// The tracker id and source name of the release this download came from (match `ReleaseRow.entryId` and `ReleaseRow.source`).
    var releaseId: String? = nil
    var releaseSource: String? = nil
    var isActive: Bool { !paused && !complete && statusKind != "error" }
}
extension Transfer {
    enum CodingKeys: String, CodingKey {
        case id, title, releaseTitle, poster, progress, status, hint, paused, complete, missing, lowSpace, downloadRate, uploadRate, totalBytes, addedUtc, folder, mediaId, canUpdateRelease, peers, files, statusKind, remaining, indeterminate, releaseId, releaseSource
    }
    /// One malformed or missing field must never void the whole queue: every field except the id falls back to a default.
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decode(String.self, forKey: .id)
        title = (try? values.decodeIfPresent(String.self, forKey: .title)) ?? ""
        releaseTitle = (try? values.decodeIfPresent(String.self, forKey: .releaseTitle)) ?? ""
        poster = (try? values.decodeIfPresent(String.self, forKey: .poster)) ?? nil
        progress = (try? values.decodeIfPresent(Double.self, forKey: .progress)) ?? 0
        status = (try? values.decodeIfPresent(String.self, forKey: .status)) ?? ""
        hint = (try? values.decodeIfPresent(String.self, forKey: .hint)) ?? ""
        paused = (try? values.decodeIfPresent(Bool.self, forKey: .paused)) ?? false
        complete = (try? values.decodeIfPresent(Bool.self, forKey: .complete)) ?? false
        missing = (try? values.decodeIfPresent(Bool.self, forKey: .missing)) ?? false
        lowSpace = (try? values.decodeIfPresent(Bool.self, forKey: .lowSpace)) ?? false
        downloadRate = (try? values.decodeIfPresent(Double.self, forKey: .downloadRate)) ?? 0
        uploadRate = (try? values.decodeIfPresent(Double.self, forKey: .uploadRate)) ?? 0
        totalBytes = (try? values.decodeIfPresent(Double.self, forKey: .totalBytes)) ?? nil
        addedUtc = (try? values.decodeIfPresent(String.self, forKey: .addedUtc)) ?? ""
        folder = (try? values.decodeIfPresent(String.self, forKey: .folder)) ?? ""
        mediaId = (try? values.decodeIfPresent(String.self, forKey: .mediaId)) ?? nil
        canUpdateRelease = (try? values.decodeIfPresent(Bool.self, forKey: .canUpdateRelease)) ?? false
        peers = (try? values.decodeIfPresent(String.self, forKey: .peers)) ?? ""
        files = (try? values.decodeIfPresent(Int.self, forKey: .files)) ?? 0
        statusKind = (try? values.decodeIfPresent(String.self, forKey: .statusKind)) ?? nil
        remaining = (try? values.decodeIfPresent(String.self, forKey: .remaining)) ?? nil
        indeterminate = (try? values.decodeIfPresent(Bool.self, forKey: .indeterminate)) ?? nil
        releaseId = (try? values.decodeIfPresent(String.self, forKey: .releaseId)) ?? nil
        releaseSource = (try? values.decodeIfPresent(String.self, forKey: .releaseSource)) ?? nil
    }
}
struct DownloadResult: Decodable { var items: [Transfer]; var updated: Bool? = nil }
struct DownloadFile: Decodable, Identifiable { var name: String; var fullPath: String; var incompletePath: String; var size: Double; var progress: Double; var id: String { fullPath } }
struct FilesResult: Decodable { var files: [DownloadFile] }
struct LanDevice: Decodable, Identifiable { var id: String; var name: String; var address: String; var paired: Bool; var fingerprint: String }
struct LanState: Decodable { var name: String; var devices: [LanDevice] }
struct PhoneState: Decodable { var urls: [String]; var code: String?; var connected: Int }
struct PhoneResponse: Decodable { var running: Bool; var state: PhoneState? }
struct PairingPrompt: Decodable, Identifiable { var id: String; var name: String; var code: String }
struct RPCError: Decodable, Error, LocalizedError { var code: String; var message: String; var errorDescription: String? { message } }
struct Envelope: Decodable { var id: String?; var result: JSONValue?; var error: RPCError?; var event: String?; var data: JSONValue? }

let nativeEnglish: [String: String] = {
    guard let url = Bundle.main.url(forResource: "en", withExtension: "json"), let data = try? Data(contentsOf: url) else { return [:] }
    return (try? JSONDecoder().decode([String: String].self, from: data)) ?? [:]
}()
func tr(_ russian: String, _ english: String? = nil) -> String { UserDefaults.standard.string(forKey: "UiLanguage") == "en" ? english ?? nativeEnglish[russian] ?? russian : russian }
func byteText(_ value: Double) -> String { let formatter = ByteCountFormatter(); formatter.countStyle = .file; return formatter.string(fromByteCount: Int64(max(0, min(value.isFinite ? value : 0, Double(Int64.max - 4096))))) }
