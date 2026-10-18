import AppKit
import SwiftUI
import UserNotifications

enum Section: String, CaseIterable, Identifiable { case featured, movies, series, games, saved, downloads, settings
    var id: String { rawValue }
    var title: String { switch self { case .featured: return tr("Актуальное", "Featured"); case .movies: return tr("Фильмы", "Movies"); case .series: return tr("Сериалы", "Series"); case .games: return tr("Игры", "Games"); case .saved: return tr("Сохранённое", "Saved"); case .downloads: return tr("Загрузки", "Downloads"); case .settings: return tr("Настройки", "Settings") } }
    var icon: String { switch self { case .featured: return "sparkles"; case .movies: return "film"; case .series: return "tv"; case .games: return "gamecontroller"; case .saved: return "heart"; case .downloads: return "arrow.down.circle"; case .settings: return "gearshape" } }
}
enum Route: Equatable { case section(Section), collection(String), detail(MediaCard), person(Person)
    static func == (a: Route, b: Route) -> Bool { a.key == b.key }
    var key: String { switch self { case .section(let v): return v.rawValue; case .collection(let id): return "collection:" + id; case .detail(let card): return "detail:" + card.id; case .person(let person): return "person:" + person.id } }
}
struct CatalogFilter: Equatable {
    var genre = ""; var country = ""; var year = 0; var rating = 0; var sort = "date"; var quality = ""; var kind = ""
    var active: Bool { self != CatalogFilter() }
    var arguments: [String: JSONValue] { ["genre": .string(genre), "country": .string(country), "year": .number(Double(year)), "rating": .number(Double(rating)), "sort": .string(sort), "kind": .string(kind)] }
}
@MainActor final class TransferStore: ObservableObject { @Published var items: [Transfer] = [] }
@MainActor final class AppModel: ObservableObject {
    let bridge = EngineBridge()
    let transfers = TransferStore()
    @Published var settings: AppSettings?
    @Published var version = ""
    @Published var connected = false
    @Published var query = ""
    @Published var submittedQuery = ""
    @Published var filter = CatalogFilter()
    @Published var page = 1
    @Published var routes: [Route] = [.section(.featured)]
    @Published var section: Section = .featured
    @Published var list: CatalogResult?
    @Published var curated: [CuratedGroup] = []
    @Published var detail: DetailResult?
    @Published var person: PersonResult?
    @Published var releases: [ReleaseRow] = []
    @Published var sources: [SourceState] = []
    @Published var releasesLoading = false
    @Published var loading = false
    @Published var error: String?
    @Published var toast: String?
    @Published var addTorrent = false
    @Published var magnet = ""
    @Published var folderRequired = false
    @Published var pairingPrompt: PairingPrompt?
    @Published var lanState: LanState?
    @Published var revision = 0
    var genres: [Choice] = []; var countries: [Choice] = []; var gameGenres: [Choice] = []
    var hiddenCards = Set<String>()
    var browseAnchor: String?
    private var cache: [String: CatalogResult] = [:]
    private var pendingReleaseCard: String?
    var route: Route { routes.last ?? .section(section) }
    var accent: Color { Color(red: 0.15, green: 0.78, blue: 0.66) }
    init() {
        bridge.onEvent = { [weak self] event, data in self?.receive(event, data) }
        bridge.onFailure = { [weak self] message in self?.error = message }
    }
    func bootstrap() async {
        do {
            let result = try await bridge.call("bootstrap", as: Bootstrap.self)
            settings = result.settings; version = result.version; genres = result.genres; countries = result.countries; gameGenres = result.gameGenres
            UserDefaults.standard.set(result.settings.uiLanguage, forKey: "UiLanguage")
            ImageStore.shared.setLite(result.settings.liteMode); connected = true; error = nil
            folderRequired = !result.settings.folderConfigured
            transfers.items = try await bridge.call("downloads", as: DownloadResult.self).items
        } catch { self.error = error.localizedDescription }
    }
    func select(_ next: Section) {
        if section == next, route == .section(next) { submittedQuery = ""; query = ""; page = 1; filter = CatalogFilter(); revision += 1 }
        section = next; routes = [.section(next)]; error = nil
    }
    func open(_ card: MediaCard) { detail = nil; person = nil; releases = []; sources = []; routes.append(.detail(card)); error = nil }
    func open(_ person: Person) { self.person = nil; routes.append(.person(person)); error = nil }
    func openCollection(_ id: String) { list = nil; routes.append(.collection(id)); error = nil }
    func back() { if routes.count > 1 { routes.removeLast(); error = nil } }
    func submit() {
        submittedQuery = query.trimmingCharacters(in: .whitespacesAndNewlines); filter = CatalogFilter(); page = 1
        let target: Section = section == .games ? .games : .movies
        section = target; routes = [.section(target)]; revision += 1
    }
    var requestKey: String { section.rawValue + submittedQuery + String(page) + String(revision) + filter.genre + filter.country + String(filter.year) + String(filter.rating) + filter.sort + filter.kind + filter.quality }
    func loadCatalog() async {
        let key = requestKey
        if let ready = cache[key] { list = ready; return }
        loading = true; error = nil; list = nil
        defer { if requestKey == key { loading = false } }
        do {
            var parameters = filter.arguments
            parameters["section"] = .string(section.rawValue); parameters["query"] = .string(submittedQuery); parameters["page"] = .number(Double(page))
            if section == .games, filter.sort == "date" { parameters["sort"] = "popular" }
            let result = try await bridge.call("catalog", parameters, as: CatalogResult.self)
            guard !Task.isCancelled, requestKey == key else { return }
            if cache.count >= 20 { cache.removeValue(forKey: cache.keys.first!) }; cache[key] = result; list = result
        } catch is CancellationError {} catch { if !Task.isCancelled, requestKey == key { self.error = error.localizedDescription } }
    }
    func loadFeatured() async {
        guard curated.isEmpty else { return }; loading = true
        defer { loading = false }
        do { let result = try await bridge.call("featured", as: FeaturedResult.self); if !Task.isCancelled { curated = result.groups } }
        catch is CancellationError {} catch { if !Task.isCancelled { self.error = error.localizedDescription } }
    }
    func loadCollection(_ id: String) async {
        loading = true; list = nil; defer { loading = false }
        do { let result = try await bridge.call("featured", ["id": .string(id)], as: CatalogResult.self); if route == .collection(id), !Task.isCancelled { list = result } }
        catch is CancellationError {} catch { if !Task.isCancelled { self.error = error.localizedDescription } }
    }
    func loadSaved() async {
        do { list = try await bridge.call("saved", as: CatalogResult.self) } catch { self.error = error.localizedDescription }
    }
    func loadDetail(_ card: MediaCard) async {
        do { let result = try await bridge.call("detail", ["id": .string(card.id)], as: DetailResult.self); if route == .detail(card), !Task.isCancelled { detail = result } }
        catch is CancellationError {} catch { if !Task.isCancelled { self.error = error.localizedDescription } }
    }
    func loadReleases(_ card: MediaCard) async {
        pendingReleaseCard = card.id; releasesLoading = true
        defer { if pendingReleaseCard == card.id { releasesLoading = false } }
        do { let result = try await bridge.call("releases", ["id": .string(card.id)], as: ReleaseResult.self); if route == .detail(card), !Task.isCancelled { releases = result.items; sources = result.sources } }
        catch is CancellationError {} catch { if !Task.isCancelled { self.error = error.localizedDescription } }
    }
    func loadPerson(_ value: Person) async {
        do { let argument = try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(value)); let result = try await bridge.call("person", ["person": argument], as: PersonResult.self); if route == .person(value), !Task.isCancelled { person = result } }
        catch is CancellationError {} catch { if !Task.isCancelled { self.error = error.localizedDescription } }
    }
    func isSaved(_ card: MediaCard) -> Bool { settings?.savedIds.contains(card.id) ?? card.saved }
    func save(_ card: MediaCard) async {
        do { _ = try await bridge.call("save", ["id": .string(card.id)]); if section == .saved { await loadSaved() } }
        catch { self.error = error.localizedDescription }
    }
    func hide(_ card: MediaCard) async {
        do { _ = try await bridge.call("hide", ["id": .string(card.id)]); hiddenCards.insert(card.id); cache.removeAll(); toast = tr("Скрыто. Вернуть можно в настройках.", "Hidden. Restore it in Settings.") }
        catch { self.error = error.localizedDescription }
    }
    func unhide(_ id: String) async { do { settings = try await bridge.call("unhide", ["id": .string(id)], as: AppSettings.self); hiddenCards.removeAll(); cache.removeAll(); curated = []; revision += 1 } catch { self.error = error.localizedDescription } }
    func set(_ key: String, _ value: JSONValue) async {
        do { settings = try await bridge.call("setSettings", [key: value], as: AppSettings.self); if let settings { UserDefaults.standard.set(settings.uiLanguage, forKey: "UiLanguage"); ImageStore.shared.setLite(settings.liteMode) }; cache.removeAll(); curated = []; revision += 1 }
        catch { self.error = error.localizedDescription }
    }
    func chooseFolder() async {
        let panel = NSOpenPanel(); panel.canChooseFiles = false; panel.canChooseDirectories = true; panel.canCreateDirectories = true; panel.allowsMultipleSelection = false
        panel.message = tr("В выбранной папке будет создана папка Ka4alka для загрузок.", "A Ka4alka download folder will be created inside the selected folder.")
        guard await panel.begin() == .OK, let folder = panel.url else { return }
        do { settings = try await bridge.call("folder", ["path": .string(folder.path)], as: AppSettings.self); folderRequired = false } catch { self.error = error.localizedDescription }
    }
    func add(_ release: ReleaseRow? = nil, card: MediaCard? = nil) async {
        if settings?.folderConfigured != true { await chooseFolder(); guard settings?.folderConfigured == true else { return } }
        do {
            var parameters: [String: JSONValue] = ["source": .string(magnet)]
            if let release { parameters["release"] = .string(release.id) }; if let card { parameters["card"] = .string(card.id) }
            _ = try await bridge.call("add", parameters); addTorrent = false; magnet = ""; toast = tr("Раздача добавлена", "Download added")
            if settings?.notifyDownloads == true { _ = try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) }
        } catch { self.error = error.localizedDescription }
    }
    func transferAction(_ method: String, id: String, deleteFiles: Bool = false) async {
        do { transfers.items = try await bridge.call(method, ["id": .string(id), "deleteFiles": .bool(deleteFiles)], as: DownloadResult.self).items } catch { self.error = error.localizedDescription }
    }
    func batch(completed: Bool, paused: Bool) async { do { transfers.items = try await bridge.call("batch", ["completed": .bool(completed), "paused": .bool(paused)], as: DownloadResult.self).items } catch { self.error = error.localizedDescription } }
    func refresh() async {
        do { _ = try await bridge.call("refresh"); cache.removeAll(); curated = []; revision += 1 } catch { self.error = error.localizedDescription }
    }
    func lan(_ action: String, _ parameters: [String: JSONValue] = [:]) async {
        do { var values = parameters; values["action"] = .string(action); lanState = try await bridge.call("lan", values, as: LanState.self) } catch { self.error = error.localizedDescription }
    }
    func approvePairing(_ accepted: Bool) async { guard let prompt = pairingPrompt else { return }; pairingPrompt = nil; await lan("approve", ["id": .string(prompt.id), "approved": .bool(accepted)]) }
    func logs() async { do { let result = try await bridge.call("logs"); NSPasteboard.general.clearContents(); NSPasteboard.general.setString(result["text"].string, forType: .string); toast = tr("Логи скопированы", "Logs copied") } catch { self.error = error.localizedDescription } }
    func visible(_ items: [MediaCard]) -> [MediaCard] { items.filter { !hiddenCards.contains($0.id) && (filter.quality.isEmpty || $0.quality == filter.quality) } }
    func card(_ id: String) -> MediaCard? { ([list?.items ?? []] + curated.map(\.items) + [detail.map { [$0.item] } ?? []]).flatMap { $0 }.first { $0.id == id } }
    private func receive(_ event: String, _ data: JSONValue) {
        switch event {
        case "downloads": if let value = try? data.decode(DownloadResult.self), value.items != transfers.items { transfers.items = value.items; NSApp.dockTile.badgeLabel = value.items.filter { !$0.paused && !$0.complete }.isEmpty ? nil : String(value.items.filter { !$0.paused && !$0.complete }.count) }
        case "personal": if let value = try? data.decode(AppSettings.self) { settings = value }
        case "releases": if case .detail(let card) = route, card.id == pendingReleaseCard, let result = try? data.decode(ReleaseResult.self) { releases = result.items; sources = result.sources; releasesLoading = !result.complete }
        case "pairing": pairingPrompt = try? data.decode(PairingPrompt.self)
        case "notice":
            let content = UNMutableNotificationContent(); content.title = data["name"].string; content.body = data["message"].string; content.sound = .default
            if settings?.notifyDownloads == true { UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil), withCompletionHandler: nil) }
        case "catalogReady": if list?.items.isEmpty != false { revision += 1 }
        default: break
        }
    }
}
