import SwiftUI

struct GameShelfDefinition: Decodable, Identifiable { var id: String; var title: String }

/// Result of loading one Steam shelf. `failed` is false when the request was only cancelled.
struct GameShelfOutcome {
    var id: String
    var result: CatalogResult?
    var failed: Bool

    @MainActor static func fetch(_ id: String, model: AppModel, force: Bool) async -> GameShelfOutcome {
        do {
            let result: CatalogResult = try await model.gameShelf(id, force: force)
            return GameShelfOutcome(id: id, result: result, failed: false)
        } catch {
            return GameShelfOutcome(id: id, result: nil, failed: model.message(for: error) != nil)
        }
    }
}

enum GameHomePhase { case loading, ready, failed }

/// A shelf that is drawn: `cards` is nil while its request is still running.
struct GameHomeShelfRow: Identifiable {
    var definition: GameShelfDefinition
    var cards: [MediaCard]?
    var id: String { definition.id }
}

struct GameHomeSortChip: Identifiable {
    var key: String
    var title: String
    var symbol: String
    var id: String { key }
}

/// The Games home: banner, sort chips, genre rail, the person's own games and the Steam shelves.
struct GameHomeView: View {
    @EnvironmentObject private var model: AppModel
    @State private var anchor: String?
    @State private var results: [String: CatalogResult] = [:]
    @State private var failedIDs = Set<String>()
    @State private var phase: GameHomePhase = .loading

    private var sortChips: [GameHomeSortChip] {
        [
            GameHomeSortChip(key: "popular", title: tr("Все игры", "All games"), symbol: "square.grid.2x2"),
            GameHomeSortChip(key: "specials", title: tr("Скидки", "On sale"), symbol: "tag"),
            GameHomeSortChip(key: "new", title: tr("Новинки", "New"), symbol: "sparkles"),
            GameHomeSortChip(key: "soon", title: tr("Скоро", "Coming soon"), symbol: "clock")
        ]
    }

    private func result(for id: String) -> CatalogResult? {
        if let loaded = results[id] { return loaded }
        return model.gameShelfCache[id]?.result
    }

    /// Slides of the banner: the first five visible games of the "popular" shelf.
    private var heroCards: [MediaCard] {
        var source: CatalogResult? = result(for: "popular")
        if source == nil, let first = model.gameShelves.first { source = result(for: first.id) }
        guard let items = source?.items else { return [] }
        var cards: [MediaCard] = []
        for card in model.visible(items).prefix(5) {
            var copy = card
            copy.genre = gameHomeGenres(without: "Windows", from: card.genre)
            cards.append(copy)
        }
        return cards
    }

    private var shelfRows: [GameHomeShelfRow] {
        var rows: [GameHomeShelfRow] = []
        for definition in model.gameShelves {
            if let loaded = result(for: definition.id) {
                let cards: [MediaCard] = model.visible(Array(loaded.items.prefix(12)))
                if !cards.isEmpty { rows.append(GameHomeShelfRow(definition: definition, cards: cards)) }
            } else if phase == .loading && !failedIDs.contains(definition.id) {
                rows.append(GameHomeShelfRow(definition: definition, cards: nil))
            }
        }
        return rows
    }

    /// Cards of the loaded shelves, so "Ваши игры" can show the full card when it is known.
    private var knownCards: [MediaCard] {
        var cards: [MediaCard] = []
        for definition in model.gameShelves {
            if let loaded = result(for: definition.id) { cards.append(contentsOf: loaded.items) }
        }
        return cards
    }

    var body: some View {
        ScrollView {
            // The banner is outside the centred column: it runs from edge to edge and up under the toolbar.
            VStack(spacing: 0) {
            hero
            LazyVStack(alignment: .leading, spacing: 0) {
                KDPageHeader(
                    tr("Игры", "Games"),
                    subtitle: tr("Каталог игр для Windows. Можно скачать здесь или передать загрузку на ПК.", "Windows game catalog. Download here or send a download to your PC.")
                )
                .padding(.horizontal, KD.gutter)
                .padding(.bottom, KD.Space.l)
                .id("games-header")
                if phase == .failed && shelfRows.isEmpty {
                    unavailable
                } else {
                    homeContent
                }
            }
            .scrollTargetLayout()
            .padding(.top, KD.Space.l)
            .padding(.bottom, KD.gutter)
            .frame(maxWidth: KD.Width.page)
            .frame(maxWidth: .infinity)
            }
        }
        // With the banner first, the page starts at the top of the window, under the toolbar.
        .ignoresSafeArea(edges: showsHero ? .top : [])
        .scrollPosition(id: $anchor, anchor: .top)
        .onAppear { if anchor == nil { anchor = model.gamesAnchor } }
        .onDisappear { model.gamesAnchor = anchor }
        .task(id: model.revision) { await load(force: false) }
    }

    // MARK: Content

    @ViewBuilder private var homeContent: some View {
        sortRow
        genreRail
        GameHomeOwnedShelf(store: model.transfers, known: knownCards)
            .id("games-owned")
        ForEach(shelfRows) { row in
            shelf(row)
                .padding(.bottom, KD.Space.xl)
                .id(row.id)
        }
    }

    /// True when a banner (or its placeholder) opens the page.
    private var showsHero: Bool { !heroCards.isEmpty || (heroCards.isEmpty && phase == .loading) }

    @ViewBuilder private var hero: some View {
        let cards: [MediaCard] = heroCards
        if !cards.isEmpty {
            CatalogSpotlightView(items: cards)
                .id("games-hero")
                .settingsLayoutProbe("games.hero")
        } else if phase == .loading {
            HeroPlaceholder()
                .id("games-hero")
                .settingsLayoutProbe("games.hero")
        }
    }

    private var sortRow: some View {
        KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
            ForEach(sortChips) { chip in
                Button { model.filter.sort = chip.key } label: { KDChipLabel(text: chip.title, systemImage: chip.symbol) }
                    .buttonStyle(KDSecondaryButtonStyle())
            }
        }
        .padding(.horizontal, KD.gutter)
        .padding(.bottom, KD.Space.l)
        .id("games-sort")
    }

    @ViewBuilder private var genreRail: some View {
        if !model.gameGenres.isEmpty {
            VStack(alignment: .leading, spacing: KD.Space.xs) {
                Text(tr("Жанры", "Genres")).font(KDFont.cardTitle).accessibilityAddTraits(.isHeader)
                    .padding(.horizontal, KD.gutter)
                KDShelf(model.gameGenres, spacing: KD.Space.xs) { genre in
                    Button { model.filter.genre = genre.key } label: { KDChipLabel(text: genre.label) }
                        .buttonStyle(KDSecondaryButtonStyle())
                }
                .featuredClippedShelf()
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(tr("Жанры", "Genres"))
            .padding(.bottom, KD.Space.xl)
            .id("games-genres")
        }
    }

    @ViewBuilder private func shelf(_ row: GameHomeShelfRow) -> some View {
        if let cards = row.cards {
            VStack(alignment: .leading, spacing: KD.Space.s) {
                KDSectionHeader(
                    title: row.definition.title,
                    actionTitle: tr("Ещё", "More"),
                    action: { model.openCollection("steam-shelf:" + row.definition.id) }
                )
                .padding(.horizontal, KD.gutter)
                KDShelf(cards) { card in
                    PosterCard(card: card, wide: true).frame(width: KD.Size.wideCardWidth)
                }
                .featuredClippedShelf()
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(row.definition.title)
        } else {
            KDShelfSkeleton(wide: true, count: 4)
                .padding(.horizontal, KD.gutter)
        }
    }

    private var unavailable: some View {
        KDEmptyState(
            title: tr("Каталог Steam пока недоступен", "The Steam catalog is not available yet"),
            systemImage: "gamecontroller",
            message: tr("Проверь подключение к интернету и повтори попытку.", "Check your internet connection and try again."),
            actionTitle: tr("Повторить", "Retry"),
            action: { model.error = nil; Task { await load(force: true) } }
        )
        .frame(maxWidth: .infinity, minHeight: 360)
    }

    // MARK: Loading

    /// Loads the list of shelves, then every shelf at once. The model keeps a 15 minute cache, so Back does not refetch.
    @MainActor private func load(force: Bool) async {
        if force || results.isEmpty { phase = .loading }
        if force { failedIDs = [] }
        await model.loadGameShelves()
        if Task.isCancelled { return }
        let definitions: [GameShelfDefinition] = model.gameShelves
        if definitions.isEmpty {
            phase = .failed
            return
        }
        let owner: AppModel = model
        var loadedAny = false
        await withTaskGroup(of: GameShelfOutcome.self) { group in
            for definition in definitions {
                let shelfID: String = definition.id
                group.addTask { await GameShelfOutcome.fetch(shelfID, model: owner, force: force) }
            }
            for await outcome in group {
                if let loaded = outcome.result {
                    results[outcome.id] = loaded
                    failedIDs.remove(outcome.id)
                    loadedAny = true
                } else if outcome.failed {
                    failedIDs.insert(outcome.id)
                }
            }
        }
        if Task.isCancelled { return }
        phase = (loadedAny || !results.isEmpty) ? .ready : .failed
    }
}

/// "Windows, Экшен" -> "Экшен". The store label "Windows" is not a genre.
func gameHomeGenres(without label: String, from genre: String) -> String {
    let parts: [String] = genre.split(separator: ",").map { (part: Substring) -> String in part.trimmingCharacters(in: .whitespaces) }
    return parts.filter { (part: String) -> Bool in !part.isEmpty && part != label }.joined(separator: ", ")
}

/// "Ваши игры": games in the download queue, newest first. It observes the queue, so progress ticks redraw only this shelf.
struct GameHomeOwnedShelf: View {
    @EnvironmentObject private var model: AppModel
    @ObservedObject var store: TransferStore
    var known: [MediaCard]

    private var cards: [MediaCard] {
        var seen = Set<String>()
        var rows: [MediaCard] = []
        for transfer in store.items {
            guard let mediaID = transfer.mediaId, mediaID.hasPrefix("games:"), !seen.contains(mediaID) else { continue }
            seen.insert(mediaID)
            if let match = known.first(where: { (card: MediaCard) -> Bool in card.id == mediaID }) {
                rows.append(match)
            } else if let match = model.card(mediaID) {
                rows.append(match)
            } else {
                rows.append(GameHomeOwnedShelf.card(from: transfer, id: mediaID))
            }
        }
        return model.visible(rows)
    }

    private static func card(from transfer: Transfer, id: String) -> MediaCard {
        MediaCard(id: id, title: transfer.title, section: "games", year: 0, rating: "—", poster: transfer.poster, description: "", genre: "", quality: nil, saved: false, platform: "Windows")
    }

    var body: some View {
        let rows: [MediaCard] = cards
        if !rows.isEmpty {
            VStack(alignment: .leading, spacing: KD.Space.s) {
                KDSectionHeader(title: tr("Ваши игры", "Your games"), subtitle: tr("То, что уже скачано или скачивается.", "Downloaded or downloading now."))
                    .padding(.horizontal, KD.gutter)
                KDShelf(rows) { card in
                    PosterCard(card: card, wide: true).frame(width: KD.Size.wideCardWidth)
                }
                .featuredClippedShelf()
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(tr("Ваши игры", "Your games"))
            .padding(.bottom, KD.Space.xl)
        }
    }
}
