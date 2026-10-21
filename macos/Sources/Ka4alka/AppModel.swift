import AppKit
import SwiftUI
import UserNotifications

enum Section: String, CaseIterable, Identifiable { case featured, movies, series, games, saved, downloads, settings
    var id: String { rawValue }
    var title: String { switch self { case .featured: return tr("Актуальное", "Featured"); case .movies: return tr("Фильмы", "Movies"); case .series: return tr("Сериалы", "Series"); case .games: return tr("Игры", "Games"); case .saved: return tr("Сохранённое", "Saved"); case .downloads: return tr("Загрузки", "Downloads"); case .settings: return tr("Настройки", "Settings") } }
    var icon: String { switch self { case .featured: return "sparkles"; case .movies: return "film"; case .series: return "tv"; case .games: return "gamecontroller"; case .saved: return "heart"; case .downloads: return "arrow.down.circle"; case .settings: return "gearshape" } }
}
enum Route: Equatable { case section(Section), search, collection(String), detail(MediaCard), person(Person)
    static func == (a: Route, b: Route) -> Bool { a.key == b.key }
    var key: String { switch self { case .section(let v): return v.rawValue; case .search: return "search"; case .collection(let id): return "collection:" + id; case .detail(let card): return "detail:" + card.id; case .person(let person): return "person:" + person.id } }
}
struct CatalogFilter: Equatable {
    var genre = ""; var country = ""; var year = 0; var rating = 0; var sort = "date"; var quality = ""; var kind = ""
    var active: Bool { self != CatalogFilter() }
    var arguments: [String: JSONValue] { ["genre": .string(genre), "country": .string(country), "year": .number(Double(year)), "rating": .number(Double(rating)), "sort": .string(sort), "kind": .string(kind), "quality": .string(quality)] }
}
/// The download queue. Views that show queue numbers observe this object, not the whole model.
@MainActor final class TransferStore: ObservableObject {
    @Published var items: [Transfer] = [] { didSet { updateDockBadge() } }
    private var shownBadge: String?
    /// Transfers that are neither paused, complete nor failed.
    var activeCount: Int { items.filter { $0.isActive }.count }
    var downloadRate: Double { items.reduce(0.0) { $0 + max(0, $1.downloadRate) } }
    var uploadRate: Double { items.reduce(0.0) { $0 + max(0, $1.uploadRate) } }
    private func updateDockBadge() {
        let count = activeCount
        let label: String? = count > 0 ? String(count) : nil
        if label == shownBadge { return }
        shownBadge = label
        NSApplication.shared.dockTile.badgeLabel = label
    }
}
@MainActor final class AppModel: ObservableObject {
    var onUpdateProgress: ((JSONValue) -> Void)?
    let bridge = EngineBridge()
    let transfers = TransferStore()
    @Published var settings: AppSettings?
    @Published var version = ""
    @Published var sidebarVisibility: NavigationSplitViewVisibility = .all
    func toggleSidebar() { sidebarVisibility = sidebarVisibility == .detailOnly ? .all : .detailOnly }
    @Published var connected = false
    @Published var bootstrapping = false
    @Published var query = ""
    @Published var submittedQuery = ""
    @Published var searchFocus = 0
    @Published var recentSearches: [RecentSearch] = []
    @Published private var searchRevision = 0
    private var searchOrigin: SearchOrigin?
    @Published var filter = CatalogFilter() { didSet { if filter != oldValue { page = 1; browseAnchor = nil } } }
    @Published var page = 1
    @Published var routes: [Route] = [.section(.featured)]
    @Published var section: Section = .featured
    @Published var list: CatalogResult?
    @Published var curated: [CuratedGroup] = []
    @Published var detail: DetailResult?
    @Published var person: PersonResult?
    @Published var releases: [ReleaseRow] = []
    @Published var sources: [SourceState] = []
    /// The card id `releases` and `sources` belong to. Use `releases(for:)` / `sources(for:)` so another title's rows never show.
    @Published var scopedReleaseRecommendation: ScopedReleaseRecommendation?
    @Published var releasesOwner: String?
    @Published var releasesLoading = false
    @Published private var loadingKeys: [String: Int] = [:]
    @Published var error: String?
    @Published var toast: String?
    @Published var addTorrent = false { didSet { if addTorrent { addError = nil } } }
    @Published var magnet = ""
    @Published var addError: String?
    @Published var folderRequired = false
    @Published var pairingPrompt: PairingPrompt?
    @Published var phoneState: PhoneResponse?
    @Published var lanState: LanState?
    @Published var revision = 0
    @Published var refreshing = false
    @Published var appearance: AppearanceMode = AppearanceMode.stored
    @Published var featuredAnchor: String?
    @Published var gamesAnchor: String?
    @Published var featuredState: LoadState = .idle
    @Published var gameShelves: [GameShelfDefinition] = []
    @Published var hiddenCards = Set<String>()
    /// Page state that must survive Back, because the views are rebuilt with `.id(route.key)`.
    /// The Saved page: the kind filter ("" = everything, otherwise a group key such as "games") and the order ("date" or "rating").
    @Published var savedKind = ""
    @Published var savedSort = "date"
    /// The Featured ranking: its tab and the loaded chart.
    @Published var rankingState = FeaturedRankingState()
    /// The page shown on each collection page, by collection id.
    @Published var collectionPages: [String: Int] = [:]
    /// The year chosen on each awards page, by award id.
    @Published var awardYears: [String: Int] = [:]
    var gameShelfCache: [String: (date: Date, result: CatalogResult)] = [:]
    var genres: [Choice] = []; var countries: [Choice] = []; var gameGenres: [Choice] = []
    var browseAnchor: String?
    private var cache: [String: CatalogResult] = [:]
    private var pendingReleaseCard: String?
    private var featuredToken = UUID()
    private var restarting = false
    /// When the engine exited recently; three exits within a minute mean it cannot stay up and we stop restarting it.
    private var engineExitTimes: [Date] = []
    private static let engineExitWindow: TimeInterval = 60
    private static let engineExitLimit = 3
    private var lastDecodeFailure = ""
    private let notificationDelegate = NotificationDelegate()
    private static let contentKeys: Set<String> = ["uiLanguage", "hidePoorQuality", "hiddenGenres", "minimumReleaseHeight", "homeCountry"]
    private static let gameShelfLifetime: TimeInterval = 15 * 60
    /// Shown while a catalog page or a collection request is in flight. The background Featured request has its own `featuredState`.
    var loading: Bool { loadingKeys.keys.contains { $0.hasPrefix("catalog:") || $0.hasPrefix("collection:") } }
    var route: Route { routes.last ?? .section(section) }
    var accent: Color { KDPalette.accent }
    init() {
        bridge.onEvent = { [weak self] event, data in self?.receive(event, data) }
        bridge.onFailure = { [weak self] message in self?.error = message }
        bridge.onExit = { [weak self] status, reason in self?.engineExited(status: status, reason: reason) }
    }

    // MARK: Errors and loading bookkeeping

    /// The text to show for a failure, or nil when the failure is not worth showing (we cancelled the request, or the engine is restarting).
    func message(for error: Error) -> String? {
        if error is CancellationError { return nil }
        if let url = error as? URLError, url.code == .cancelled { return nil }
        if let rpc = error as? RPCError {
            // The engine answers "cancelled" for its own 90 s request limit; a cancel we sent never gets here.
            if rpc.code == "cancelled" { return Task.isCancelled ? nil : tr("Источник не ответил вовремя.", "The source did not respond in time.") }
            if rpc.code == "disabled" { return nil }
            if rpc.code == "engine-exit", restarting { return nil }
        }
        return error.localizedDescription
    }
    func fail(_ error: Error) {
        if let text = message(for: error) { self.error = text }
    }
    private func beginLoading(_ key: String) { loadingKeys[key, default: 0] += 1 }
    private func endLoading(_ key: String) {
        guard let count = loadingKeys[key] else { return }
        if count <= 1 { loadingKeys.removeValue(forKey: key) } else { loadingKeys[key] = count - 1 }
    }

    // MARK: Start-up and engine lifecycle

    func bootstrap() async {
        guard !bootstrapping else { return }
        bootstrapping = true
        error = nil
        defer { bootstrapping = false }
        do {
            let result = try await bridge.call("bootstrap", as: Bootstrap.self)
            settings = result.settings; version = result.version; genres = result.genres; countries = result.countries; gameGenres = result.gameGenres
            UserDefaults.standard.set(result.settings.uiLanguage, forKey: "UiLanguage")
            ImageStore.shared.setLite(result.settings.liteMode); connected = true; error = nil
            folderRequired = !result.settings.folderConfigured
            seedHidden(from: result.settings)
            migrateAppearance(from: result.settings)
            do { transfers.items = try await bridge.call("downloads", as: DownloadResult.self).items }
            catch { fail(error) }
            if result.settings.notifyDownloads { Task { _ = await self.enableNotifications() } }
            if result.settings.lanEnabled { Task { await self.lan("discover") } }
        } catch { fail(error) }
    }
    private func engineExited(status: Int32, reason: Process.TerminationReason) {
        logicLog("engine exited, status \(status), reason \(reason.rawValue)")
        connected = false
        list = nil; curated = []; detail = nil; person = nil; releases = []; sources = []; releasesOwner = nil
        cache.removeAll(); gameShelfCache.removeAll(); gameShelves = []
        featuredState = .idle
        let now = Date()
        engineExitTimes = engineExitTimes.filter { now.timeIntervalSince($0) < Self.engineExitWindow }
        engineExitTimes.append(now)
        if engineExitTimes.count >= Self.engineExitLimit {
            error = tr("Движок остановился и не запускается. Открой приложение заново.", "The engine stopped and cannot be started. Reopen the application.")
            return
        }
        restartEngine()
    }
    /// Starts the engine again and runs bootstrap, up to three times with 1, 3 and 8 second pauses.
    private func restartEngine() {
        guard !restarting else { return }
        restarting = true
        Task {
            let delays: [Double] = [1, 3, 8]
            for delay in delays {
                try? await Task.sleep(for: .seconds(delay))
                await self.bootstrap()
                if self.connected { break }
            }
            self.restarting = false
            if self.connected {
                ImageStore.shared.clearFailures()
                self.revision += 1
                self.toast = tr("Движок перезапущен", "The engine was restarted")
            } else {
                self.error = tr("Движок остановился и не запускается. Открой приложение заново.", "The engine stopped and cannot be started. Reopen the application.")
            }
        }
    }
    private func seedHidden(from value: AppSettings) {
        hiddenCards.formUnion(value.hidden.map { $0.id })
    }
    /// First run after the appearance setting appeared: take over the old light/dark switch once.
    private func migrateAppearance(from value: AppSettings) {
        if !AppearanceMode.hasStored {
            let mode: AppearanceMode = value.light ? .light : .dark
            UserDefaults.standard.set(mode.rawValue, forKey: AppearanceMode.defaultsKey)
            appearance = mode
        }
        NSApplication.shared.appearance = appearance.nsAppearance
    }
    func setAppearance(_ mode: AppearanceMode) {
        appearance = mode
        UserDefaults.standard.set(mode.rawValue, forKey: AppearanceMode.defaultsKey)
        NSApplication.shared.appearance = mode.nsAppearance
        // "System" has no engine equivalent: the engine keeps the last explicit light or dark choice.
        switch mode {
        case .light: Task { await self.set("light", .bool(true)) }
        case .dark: Task { await self.set("light", .bool(false)) }
        case .system: break
        }
    }

    // MARK: Navigation

    func select(_ next: Section) {
        let leavingSearch = searchOrigin != nil
        searchOrigin = nil
        if section == next, route == .section(next) {
            submittedQuery = ""; query = ""; page = 1; filter = CatalogFilter(); revision += 1
        } else if section != next || leavingSearch {
            submittedQuery = ""; query = ""; filter = CatalogFilter(); page = 1; browseAnchor = nil; list = nil
        }
        section = next; routes = [.section(next)]; error = nil
    }
    func open(_ card: MediaCard) { detail = nil; person = nil; releases = []; sources = []; releasesOwner = nil; routes.append(.detail(card)); error = nil }
    func open(_ person: Person) { self.person = nil; routes.append(.person(person)); error = nil }
    func openCollection(_ id: String) {
        // A fresh open starts at the first page and the newest year; Back keeps what the person had chosen.
        collectionPages[id] = nil
        awardYears[id] = nil
        list = nil; routes.append(.collection(id)); error = nil
    }
    func back() {
        guard routes.count > 1 else { return }
        let leavingSearch = route == .search
        routes.removeLast(); error = nil
        if leavingSearch, let origin = searchOrigin {
            section = origin.section; query = origin.query; submittedQuery = origin.submittedQuery
            filter = origin.filter; page = origin.page; browseAnchor = origin.anchor; list = origin.list
            searchOrigin = nil
        }
    }
    /// Search is a navigation destination, not a modal: Back from a result keeps its query and results.
    func openSearch() {
        if let index = routes.firstIndex(of: .search) {
            routes = Array(routes.prefix(index + 1))
        } else {
            searchOrigin = SearchOrigin(section: section, query: query, submittedQuery: submittedQuery,
                                        filter: filter, page: page, anchor: browseAnchor, list: list)
            section = section == .games ? .games : .movies
            query = ""; submittedQuery = ""; filter = CatalogFilter(); page = 1; browseAnchor = nil; list = nil
            routes.append(.search)
        }
        error = nil; searchFocus += 1
    }
    func setSearchGames(_ games: Bool) {
        let target: Section = games ? .games : .movies
        guard section != target else { return }
        section = target; filter = CatalogFilter(); page = 1; browseAnchor = nil; list = nil
    }
    /// Opens a kachalka:// link (the title page) or hands a magnet link / torrent file to the add sheet.
    /// Returns false only when a kachalka:// link could not be opened.
    @discardableResult func openURL(_ url: URL) async -> Bool {
        if url.scheme == "kachalka" {
            do {
                let card = try await bridge.call("openLink", ["url": .string(url.absoluteString)], as: MediaCard.self)
                open(card)
                return true
            } catch {
                fail(error)
                return false
            }
        }
        magnet = url.isFileURL ? url.path : url.absoluteString
        addTorrent = true
        return true
    }
    func share(_ card: MediaCard) async {
        do { let result = try await bridge.call("share", ["id": .string(card.id)]); let link = result["url"].string
            guard !link.isEmpty else { return }; NSPasteboard.general.clearContents(); NSPasteboard.general.setString(link, forType: .string); toast = tr("Ссылка скопирована", "Link copied")
        } catch { fail(error) }
    }
    /// Runs the search for `query` (also with an empty query, which shows the plain catalog), or opens a pasted kachalka:// link.
    func submit() {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        if q.hasPrefix("kachalka://"), let url = URL(string: q) {
            Task {
                let opened = await self.openURL(url)
                if opened { self.query = "" }
            }
            return
        }
        submittedQuery = q; filter = CatalogFilter(); page = 1; browseAnchor = nil
        let target: Section = section == .games ? .games : .movies
        section = target
        if route != .search { routes = [.section(target)]; searchOrigin = nil }
        if !q.isEmpty {
            let entry = RecentSearch(query: q, games: target == .games)
            recentSearches.removeAll { $0.games == entry.games && $0.query.caseInsensitiveCompare(q) == .orderedSame }
            recentSearches.insert(entry, at: 0)
            recentSearches = Array(recentSearches.prefix(8))
        }
        if route == .search { searchRevision += 1 } else { revision += 1 }
    }
    /// The Return key in the search field: does nothing for an empty or blank query.
    func submitSearch() {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty else { return }
        submit()
    }
    /// The search field was cleared. On a section page the catalog is reloaded without the query; on a title, person or
    /// collection page only the stored search is reset and the route stays where it is.
    func clearSearch() {
        query = ""
        guard !submittedQuery.isEmpty else { return }
        if route == .search {
            submittedQuery = ""; filter = CatalogFilter(); page = 1; browseAnchor = nil; list = nil
            return
        }
        if case .section = route {
            submit()
        } else {
            submittedQuery = ""; filter = CatalogFilter(); page = 1; browseAnchor = nil
        }
    }

    // MARK: Catalog and pages

    var requestKey: String {
        let parts: [String] = [section.rawValue, submittedQuery, String(page), String(revision), filter.genre, filter.country, String(filter.year), String(filter.rating), filter.sort, filter.kind, filter.quality]
        return parts.joined(separator: "|") + (routes.contains(.search) ? "|search:" + String(searchRevision) : "")
    }
    func loadCatalog() async {
        let key = requestKey
        if let ready = cache[key] { list = ready; return }
        let loadingKey = "catalog:" + key
        beginLoading(loadingKey); error = nil; list = nil
        defer { endLoading(loadingKey) }
        do {
            var parameters = filter.arguments
            parameters["section"] = .string(section.rawValue); parameters["query"] = .string(submittedQuery); parameters["page"] = .number(Double(page))
            if section == .games, filter.sort == "date" { parameters["sort"] = "popular" }
            let result = try await bridge.call("catalog", parameters, as: CatalogResult.self)
            guard !Task.isCancelled, requestKey == key else { return }
            if cache.count >= 20, let oldest = cache.keys.first { cache.removeValue(forKey: oldest) }
            cache[key] = result; list = result
        } catch { if requestKey == key { fail(error) } }
    }
    /// Loads the Featured shelves. `force` keeps the shelves that are on screen until the new ones arrive.
    func loadFeatured(force: Bool = false) async {
        if !force, !curated.isEmpty { return }
        let token = UUID(); featuredToken = token
        if curated.isEmpty { featuredState = .loading }
        beginLoading("featured")
        defer { endLoading("featured") }
        do {
            let result = try await bridge.call("featured", as: FeaturedResult.self)
            guard !Task.isCancelled, featuredToken == token else {
                if featuredToken == token, featuredState == .loading { featuredState = .idle }
                return
            }
            curated = result.groups
            featuredState = result.groups.isEmpty ? .empty : .idle
        } catch {
            guard featuredToken == token else { return }
            if let text = message(for: error) {
                if curated.isEmpty { featuredState = .failed(text) } else { self.error = text }
            } else if featuredState == .loading {
                featuredState = .idle
            }
        }
    }
    func loadGameShelves() async {
        if !gameShelves.isEmpty { return }
        do { gameShelves = try await bridge.call("gameShelves", as: LogicGameShelfList.self).items }
        catch { fail(error) }
    }
    /// One Steam shelf, cached for 15 minutes so Back does not refetch the whole Games home.
    func gameShelf(_ id: String, force: Bool = false) async throws -> CatalogResult {
        if !force, let hit = gameShelfCache[id], Date().timeIntervalSince(hit.date) < AppModel.gameShelfLifetime { return hit.result }
        let result = try await bridge.call("gameShelf", ["id": .string(id)], as: CatalogResult.self)
        gameShelfCache[id] = (date: Date(), result: result)
        return result
    }
    func loadCollection(_ id: String, page: Int = 1) async {
        let loadingKey = "collection:" + id + ":" + String(page)
        beginLoading(loadingKey); list = nil
        defer { endLoading(loadingKey) }
        do {
            let result = try await bridge.call("featured", ["id": .string(id), "page": .number(Double(page))], as: CatalogResult.self)
            if route == .collection(id), !Task.isCancelled { list = result }
        } catch { if route == .collection(id) { fail(error) } }
    }
    /// Keeps the list that is already on screen until the new one arrives, so the Saved grid keeps its scroll position.
    func loadSaved() async {
        await fetchSaved()
    }
    private func fetchSaved() async {
        do {
            let result = try await bridge.call("saved", as: CatalogResult.self)
            if !Task.isCancelled { list = result }
        } catch { fail(error) }
    }
    func loadDetail(_ card: MediaCard) async {
        do {
            let result = try await bridge.call("detail", ["id": .string(card.id)], as: DetailResult.self)
            if route == .detail(card), !Task.isCancelled { detail = result }
        } catch { if route == .detail(card) { fail(error) } }
    }
    func loadReleases(_ card: MediaCard) async {
        pendingReleaseCard = card.id
        if releasesOwner != card.id { releases = []; sources = []; releasesOwner = card.id }
        releasesLoading = true
        defer { if pendingReleaseCard == card.id { releasesLoading = false } }
        do {
            let result = try await bridge.call("releases", ["id": .string(card.id)], as: ReleaseResult.self)
            if route == .detail(card), !Task.isCancelled { releases = result.items; sources = result.sources; releasesOwner = card.id }
        } catch { if route == .detail(card) { fail(error) } }
    }
    func releases(for card: MediaCard) -> [ReleaseRow] { releasesOwner == card.id ? releases : [] }
    func recommendedRelease(for card: MediaCard) -> ReleaseRow? {
        let rows = releases(for: card)
        if let selection = scopedReleaseRecommendation, selection.owner == card.id {
            guard let id = selection.id else { return nil }
            return rows.first { $0.id == id && $0.downloadable }
        }
        return rows.first { $0.recommended && $0.downloadable }
    }
    func sources(for card: MediaCard) -> [SourceState] { releasesOwner == card.id ? sources : [] }
    func loadPerson(_ value: Person) async {
        person = nil
        do {
            let argument = try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(value))
            let result = try await bridge.call("person", ["person": argument], as: PersonResult.self)
            if route == .person(value), !Task.isCancelled { person = result }
        } catch { if route == .person(value) { fail(error) } }
    }
    func personResult(for value: Person) -> PersonResult? {
        guard let current = person, current.person.id == value.id else { return nil }
        return current
    }

    // MARK: Personal data

    func isSaved(_ card: MediaCard) -> Bool { settings?.savedIds.contains(card.id) ?? card.saved }
    func save(_ card: MediaCard) async {
        do { _ = try await bridge.call("save", ["id": .string(card.id)]); if section == .saved { await fetchSaved() } }
        catch { fail(error) }
    }
    @discardableResult func hide(_ card: MediaCard) async -> Bool {
        do {
            _ = try await bridge.call("hide", ["id": .string(card.id)])
            hiddenCards.insert(card.id)
            toast = tr("Скрыто. Вернуть можно в настройках.", "Hidden. Restore it in Settings.")
            return true
        } catch { fail(error); return false }
    }
    func unhide(_ id: String) async {
        do {
            let updated = try await bridge.call("unhide", ["id": .string(id)], as: AppSettings.self)
            settings = updated; hiddenCards.removeAll(); seedHidden(from: updated)
            invalidateContent()
        } catch { fail(error) }
    }
    /// Everything the engine built from content settings is stale: shelves, pages and Steam shelves.
    private func invalidateContent() {
        cache.removeAll(); gameShelfCache.removeAll(); curated = []; featuredState = .idle; revision += 1
        rankingState = FeaturedRankingState(); collectionPages = [:]; awardYears = [:]
    }
    func set(_ key: String, _ value: JSONValue) async { await setMany([key: value]) }
    /// One `setSettings` call for several keys. Only content settings (language, quality, genres, home country) rebuild the pages.
    func setMany(_ values: [String: JSONValue]) async {
        guard !values.isEmpty else { return }
        do {
            let updated = try await bridge.call("setSettings", values, as: AppSettings.self)
            settings = updated
            UserDefaults.standard.set(updated.uiLanguage, forKey: "UiLanguage")
            ImageStore.shared.setLite(updated.liteMode)
            let changesContent = !AppModel.contentKeys.isDisjoint(with: values.keys)
            if values["uiLanguage"] != nil {
                do {
                    let localized = try await bridge.call("bootstrap", as: Bootstrap.self)
                    genres = localized.genres; countries = localized.countries; gameGenres = localized.gameGenres
                } catch { fail(error) }
                gameShelves = []
                savedKind = ""; savedSort = "date"
            }
            if changesContent { invalidateContent() }
        } catch { fail(error) }
    }
    func chooseFolder() async {
        let panel = NSOpenPanel(); panel.canChooseFiles = false; panel.canChooseDirectories = true; panel.canCreateDirectories = true; panel.allowsMultipleSelection = false
        panel.message = tr("В выбранной папке будет создана папка Ka4alka для загрузок.", "A Ka4alka download folder will be created inside the selected folder.")
        guard await panel.begin() == .OK, let folder = panel.url else { return }
        do { settings = try await bridge.call("folder", ["path": .string(folder.resolvingSymlinksInPath().path)], as: AppSettings.self); folderRequired = false; error = nil } catch { fail(error) }
    }

    // MARK: Downloads and devices

    /// Adds a release (or a pasted magnet / torrent path). Without a release the failure text goes to `addError`
    /// (the add sheet and the drop handler read it); a release download from a detail page has no such place, and the
    /// error banner's Retry cannot repeat an add, so its failure goes to `toast`.
    @discardableResult func add(_ release: ReleaseRow? = nil, card: MediaCard? = nil, source: String? = nil) async -> Bool {
        addError = nil
        let manual = release == nil
        if settings?.folderConfigured != true {
            await chooseFolder()
            guard settings?.folderConfigured == true else {
                reportAddFailure(tr("Выбери папку для загрузок.", "Choose a download folder first."), manual: manual)
                return false
            }
        }
        var parameters: [String: JSONValue] = [:]
        if let release { parameters["release"] = .string(release.id) }
        if let card { parameters["card"] = .string(card.id) }
        if let source { parameters["source"] = .string(source) } else if manual { parameters["source"] = .string(magnet) }
        do {
            _ = try await bridge.call("add", parameters)
            addTorrent = false; magnet = ""; addError = nil
            toast = tr("Раздача добавлена", "Download added")
            if settings?.notifyDownloads == true { Task { _ = await self.enableNotifications() } }
            if manual { select(.downloads) }
            return true
        } catch {
            reportAddFailure(message(for: error), manual: manual)
            return false
        }
    }
    private func reportAddFailure(_ text: String?, manual: Bool) {
        guard let text else { return }
        if manual { addError = text } else { toast = text }
    }
    /// Runs a queue command and replaces the queue with the answer. Returns false when the call failed (the failure is in `error`).
    /// For "refreshTorrent" it returns true only when the engine swapped the torrent; "nothing changed" is false with no error.
    @discardableResult func transferAction(_ method: String, id: String, deleteFiles: Bool = false) async -> Bool {
        do {
            let result = try await bridge.call(method, ["id": .string(id), "deleteFiles": .bool(deleteFiles)], as: DownloadResult.self)
            transfers.items = result.items
            return result.updated ?? true
        } catch {
            fail(error)
            return false
        }
    }
    func batch(completed: Bool, paused: Bool) async {
        do { transfers.items = try await bridge.call("batch", ["completed": .bool(completed), "paused": .bool(paused)], as: DownloadResult.self).items } catch { fail(error) }
    }
    /// Asks the engine to re-check the catalog. The shelves on screen stay until the new ones arrive.
    /// Returns the failure text, or nil when the catalog was refreshed (or a refresh is already running, or the request was cancelled).
    @discardableResult func refresh() async -> String? {
        guard !refreshing else { return nil }
        refreshing = true
        defer { refreshing = false }
        toast = tr("Проверяем каталог…", "Checking the catalog…")
        do {
            _ = try await bridge.call("refresh")
            cache.removeAll(); gameShelfCache.removeAll(); ImageStore.shared.clearFailures(); revision += 1
            await loadFeatured(force: true)
            toast = tr("Каталог обновлён.", "Catalog updated.")
            return nil
        } catch {
            toast = nil
            let text = message(for: error)
            fail(error)
            return text
        }
    }
    /// Returns false when the engine call failed (the failure is in `error`).
    @discardableResult func lan(_ action: String, _ parameters: [String: JSONValue] = [:]) async -> Bool {
        var values = parameters; values["action"] = .string(action)
        let deviceID = parameters["device"]?.string ?? ""
        do {
            lanState = try await bridge.call("lan", values, as: LanState.self)
            if action == "send" {
                let name = lanState?.devices.first(where: { $0.id == deviceID })?.name ?? ""
                toast = name.isEmpty ? tr("Отправлено", "Sent") : tr("Отправлено на «\(name)»", "Sent to “\(name)”")
            }
            return true
        } catch {
            fail(error)
            return false
        }
    }
    @discardableResult func phone(_ action: String) async -> Bool {
        do {
            phoneState = try await bridge.call("phone", ["action": .string(action)], as: PhoneResponse.self)
            return true
        } catch { fail(error); return false }
    }
    func approvePairing(_ accepted: Bool) async { guard let prompt = pairingPrompt else { return }; pairingPrompt = nil; await lan("approve", ["id": .string(prompt.id), "approved": .bool(accepted)]) }
    /// Copies the diagnostic report to the clipboard. Returns false when it could not be built (the failure is in `error`).
    @discardableResult func logs() async -> Bool {
        do {
            let result = try await bridge.call("logs")
            NSPasteboard.general.clearContents(); NSPasteboard.general.setString(result["text"].string, forType: .string); toast = tr("Логи скопированы", "Logs copied")
            return true
        } catch {
            fail(error)
            return false
        }
    }
    func visible(_ items: [MediaCard]) -> [MediaCard] { items.filter { !hiddenCards.contains($0.id) } }
    func card(_ id: String) -> MediaCard? { ([list?.items ?? []] + curated.map(\.items) + [detail.map { [$0.item] } ?? []]).flatMap { $0 }.first { $0.id == id } }

    // MARK: Notifications

    /// UNUserNotificationCenter needs an application bundle; unit tests and bare executables have none.
    private static var canNotify: Bool { Bundle.main.bundleURL.pathExtension == "app" }
    private func installNotificationDelegate() {
        guard AppModel.canNotify else { return }
        notificationDelegate.onOpenDownloads = { [weak self] in
            let owner = self
            Task { @MainActor in
                NSApplication.shared.activate(ignoringOtherApps: true)
                owner?.select(.downloads)
            }
        }
        UNUserNotificationCenter.current().delegate = notificationDelegate
    }
    func notificationStatus() async -> UNAuthorizationStatus {
        guard AppModel.canNotify else { return .notDetermined }
        let current = await UNUserNotificationCenter.current().notificationSettings()
        return current.authorizationStatus
    }
    /// Asks macOS for permission (the system dialog appears only the first time). Returns whether notifications are allowed.
    func enableNotifications() async -> Bool {
        guard AppModel.canNotify else { return false }
        installNotificationDelegate()
        do { return try await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) }
        catch { return false }
    }
    func sendTestNotification() async {
        guard AppModel.canNotify else { return }
        installNotificationDelegate()
        var status = await notificationStatus()
        if status == .notDetermined { _ = await enableNotifications(); status = await notificationStatus() }
        guard status == .authorized || status == .provisional else {
            toast = tr("Уведомления выключены в настройках macOS.", "Notifications are turned off in macOS settings.")
            return
        }
        let content = UNMutableNotificationContent()
        content.title = tr("Качалка", "Ka4alka"); content.body = tr("Уведомления работают.", "Notifications are working."); content.sound = UNNotificationSound.default
        let request = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        do { try await UNUserNotificationCenter.current().add(request) }
        catch { logicLog("test notification failed: \(error.localizedDescription)") }
    }
    private func postNotice(_ data: JSONValue) {
        guard settings?.notifyDownloads == true, AppModel.canNotify else { return }
        installNotificationDelegate()
        let kind = data["kind"].string.lowercased()
        let name = data["name"].string; let message = data["message"].string
        let content = UNMutableNotificationContent()
        if kind.hasPrefix("complet") { content.title = tr("Загрузка завершена", "Download complete") }
        else if kind.contains("space") { content.title = tr("Недостаточно места", "Not enough space") }
        else { content.title = tr("Не удалось загрузить", "Download failed") }
        if message.isEmpty { content.body = name } else { content.subtitle = name; content.body = message }
        content.sound = UNNotificationSound.default
        content.userInfo = ["downloadId": data["downloadId"].string]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil), withCompletionHandler: nil)
    }

    // MARK: Engine events

    private func receive(_ event: String, _ data: JSONValue) {
        switch event {
        case "updateProgress": onUpdateProgress?(data)
        case "downloads": receiveDownloads(data)
        case "personal":
            if let value = try? data.decode(AppSettings.self) { settings = value; seedHidden(from: value) }
        case "releases": receiveReleases(data)
        case "pairing": pairingPrompt = try? data.decode(PairingPrompt.self)
        case "notice": postNotice(data)
        case "catalogReady": catalogBecameReady()
        default: break
        }
    }
    /// A progressive release list. An event that names another title than the one being loaded is a late answer and is dropped.
    private func receiveReleases(_ data: JSONValue) {
        guard case .detail(let card) = route, card.id == pendingReleaseCard else { return }
        let named: String = data["card"].string
        if !named.isEmpty, named != pendingReleaseCard { return }
        guard let result = try? data.decode(ReleaseResult.self) else { return }
        releases = result.items; sources = result.sources; releasesOwner = card.id; releasesLoading = !result.complete
    }
    private func receiveDownloads(_ data: JSONValue) {
        do {
            let value = try data.decode(DownloadResult.self)
            if value.items != transfers.items { transfers.items = value.items }
        } catch {
            // A queue that stops updating must leave a trace, but one line per distinct failure is enough.
            let text = String(describing: error)
            if text != lastDecodeFailure { lastDecodeFailure = text; logicLog("downloads payload could not be decoded: " + text) }
        }
    }
    /// The engine rebuilt its catalog (bundled seed, then the fresh index): drop pages built from older data.
    private func catalogBecameReady() {
        cache.removeAll()
        ImageStore.shared.clearFailures()
        let browsing = section == .movies || section == .series
        if browsing, list?.items.isEmpty != false { revision += 1 }
        Task { await self.loadFeatured(force: true) }
    }
}
