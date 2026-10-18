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
}
struct MediaCard: Codable, Identifiable, Equatable {
    var id: String; var title: String; var section: String; var year: Int; var rating: String; var poster: String?; var description: String; var genre: String; var quality: String?; var saved: Bool; var platform: String?; var store: StoreCard? = nil
}
struct Choice: Codable, Identifiable { var key: String; var label: String; var id: String { key } }
struct HiddenTitle: Codable, Identifiable { var id: String; var title: String }
struct AppSettings: Codable {
    var folder: String; var folderConfigured: Bool; var uiLanguage: String; var light: Bool; var liteMode: Bool
    var autoResumeDownloads: Bool; var autoRecoverDownloads: Bool; var notifyDownloads: Bool; var checkForUpdates: Bool; var autoUpdate: Bool
    var hidePoorQuality: Bool; var minimumReleaseHeight: Int; var maxDownloadKbps: Int; var maxUploadKbps: Int; var lanEnabled: Bool; var downloadSort: String
    var hidden: [HiddenTitle]; var hiddenGenres: [String]; var savedIds: [String]
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
struct DetailResult: Decodable { var item: MediaCard; var backdrop: String?; var people: [Person]; var awards: [CinemaAward]; var game: GameDetail? }
struct PersonResult: Decodable { var person: Person; var description: String; var items: [MediaCard]; var sourceUrl: String? }
struct ReleaseRow: Decodable, Identifiable, Equatable { var id: String; var title: String; var source: String; var size: Double?; var seeds: Int?; var leechers: Int?; var quality: String; var type: String; var audio: String; var studios: [String]; var audioTypes: [String]; var codec: String; var subs: String; var hdr: String; var poor: Bool; var downloadable: Bool }
struct SourceState: Decodable, Identifiable { var name: String; var state: String; var id: String { name } }
struct ReleaseResult: Decodable { var request: String?; var items: [ReleaseRow]; var complete: Bool; var sources: [SourceState] }
struct Transfer: Decodable, Identifiable, Equatable { var id: String; var title: String; var releaseTitle: String; var poster: String?; var progress: Double; var status: String; var hint: String; var paused: Bool; var complete: Bool; var missing: Bool; var lowSpace: Bool; var downloadRate: Double; var uploadRate: Double; var totalBytes: Double; var addedUtc: String; var folder: String; var mediaId: String?; var canUpdateRelease: Bool; var peers: String; var files: Int }
struct DownloadResult: Decodable { var items: [Transfer] }
struct DownloadFile: Decodable, Identifiable { var name: String; var fullPath: String; var incompletePath: String; var size: Double; var progress: Double; var id: String { fullPath } }
struct FilesResult: Decodable { var files: [DownloadFile] }
struct LanDevice: Decodable, Identifiable { var id: String; var name: String; var address: String; var paired: Bool; var fingerprint: String }
struct LanState: Decodable { var name: String; var devices: [LanDevice] }
struct PairingPrompt: Decodable, Identifiable { var id: String; var name: String; var code: String }
struct RPCError: Decodable, Error, LocalizedError { var code: String; var message: String; var errorDescription: String? { message } }
struct Envelope: Decodable { var id: String?; var result: JSONValue?; var error: RPCError?; var event: String?; var data: JSONValue? }

let nativeEnglish: [String: String] = {
    guard let url = Bundle.main.url(forResource: "en", withExtension: "json"), let data = try? Data(contentsOf: url) else { return [:] }
    return (try? JSONDecoder().decode([String: String].self, from: data)) ?? [:]
}()
func tr(_ russian: String, _ english: String? = nil) -> String { UserDefaults.standard.string(forKey: "UiLanguage") == "en" ? english ?? nativeEnglish[russian] ?? russian : russian }
func byteText(_ value: Double) -> String { let formatter = ByteCountFormatter(); formatter.countStyle = .file; return formatter.string(fromByteCount: Int64(max(0, min(value.isFinite ? value : 0, Double(Int64.max - 4096))))) }
