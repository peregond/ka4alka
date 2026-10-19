import SwiftUI

// Building blocks of the Актуальное page: the digest, the lead rubric, ordinary shelves and the ranking with tabs.
// FeaturedView.swift composes them. Every top-level name starts with "Featured" / "featured".

// MARK: Selection

/// One curated group together with the cards the person may see (hidden titles removed).
struct FeaturedGroupSlice: Identifiable {
    var group: CuratedGroup
    var cards: [MediaCard]
    var id: String { group.id }
    var isAward: Bool { group.id.hasPrefix("award") }
    var isGamesOnly: Bool { !cards.isEmpty && cards.allSatisfy { (card: MediaCard) -> Bool in card.section == "games" } }
}

struct FeaturedDigestStory: Identifiable {
    var card: MediaCard
    var kicker: String
    var id: String { card.id }
}

/// What the page shows, decided once per redraw: banner slides, digest stories and the groups to draw.
/// The lead story of a lead rubric is excluded from the banner, the digest and every poster row.
struct FeaturedPlan {
    var banner: [MediaCard] = []
    var digest: [FeaturedDigestStory] = []
    var display: [FeaturedGroupSlice] = []

    init(slices: [FeaturedGroupSlice]) {
        var leadIDs = Set<String>()
        for slice in slices where slice.group.layout == "lead" {
            if let first = slice.cards.first { leadIDs.insert(first.id) }
        }
        var used = leadIDs
        var slides: [MediaCard] = []
        for slice in slices where !slice.isAward {
            if slides.count >= 5 { break }
            let found: MediaCard? = slice.cards.first { (card: MediaCard) -> Bool in card.section != "games" && !used.contains(card.id) }
            if let card = found {
                slides.append(card)
                used.insert(card.id)
            }
        }
        banner = slides
        digest = FeaturedPlan.pickDigest(slices: slices, excluded: used)
        var rows: [FeaturedGroupSlice] = []
        for slice in slices {
            if slice.group.layout == "lead" || slice.group.layout == "ranking" {
                rows.append(slice)
                continue
            }
            let remaining: [MediaCard] = slice.cards.filter { (card: MediaCard) -> Bool in !leadIDs.contains(card.id) }
            if !remaining.isEmpty { rows.append(FeaturedGroupSlice(group: slice.group, cards: remaining)) }
        }
        display = rows
    }

    /// Three stories from different groups, preferring cards that have a description; relaxes the rules when the catalog is thin.
    private static func pickDigest(slices: [FeaturedGroupSlice], excluded: Set<String>) -> [FeaturedDigestStory] {
        var stories: [FeaturedDigestStory] = []
        var taken = excluded
        var groupsUsed = Set<String>()
        let passes: [(requireDescription: Bool, distinctGroups: Bool)] = [(true, true), (false, true), (false, false)]
        for pass in passes {
            for slice in slices where stories.count < 3 && !slice.isAward {
                if pass.distinctGroups && groupsUsed.contains(slice.group.id) { continue }
                let found: MediaCard? = slice.cards.first { (card: MediaCard) -> Bool in
                    if taken.contains(card.id) { return false }
                    if pass.requireDescription && card.description.isEmpty { return false }
                    return true
                }
                guard let card = found else { continue }
                stories.append(FeaturedDigestStory(card: card, kicker: slice.group.title))
                taken.insert(card.id)
                groupsUsed.insert(slice.group.id)
            }
        }
        return stories
    }
}

// MARK: Shared pieces

enum FeaturedLayout {
    static let leadPosterWidth: CGFloat = 150
    static let leadPosterHeight: CGFloat = 225
    static let rowThumbWidth: CGFloat = 46
    static let rowThumbHeight: CGFloat = 69
    static let digestPosterHeight: CGFloat = 138
    static let rankNumeralSize: CGFloat = 88
    /// Lifts the ranking numeral from the card's text area up to the poster's bottom edge.
    static let rankNumeralLift: CGFloat = 52
    /// The lead rubric puts the list beside the lead story only when its content is at least this wide.
    static let leadSideBySideMinWidth: CGFloat = 860
    /// Vertical room kept inside the clip of a shelf so the hover ring and shadow of a poster are not cut off.
    static let shelfClipInset: CGFloat = 8
}

extension View {
    /// KDShelf scrolls edge to edge and does not clip. On a window wider than the page column the posters would slide
    /// out into the empty side margins, so a shelf in the capped column is clipped to the column.
    func featuredClippedShelf() -> some View {
        self
            .padding(.vertical, FeaturedLayout.shelfClipInset)
            .clipShape(Rectangle())
            .padding(.vertical, -FeaturedLayout.shelfClipInset)
    }
}

/// "Драма, Триллер" -> "Драма". The store label "Windows" is not a genre.
func featuredFirstGenre(_ genre: String) -> String {
    let parts: [String] = genre.split(separator: ",").map { (part: Substring) -> String in part.trimmingCharacters(in: .whitespaces) }
    for part in parts where !part.isEmpty && part != "Windows" { return part }
    return ""
}

func featuredMetaText(_ card: MediaCard) -> String {
    let year: String = card.year > 0 ? String(card.year) : ""
    let parts: [String] = [year, featuredFirstGenre(card.genre)].filter { (part: String) -> Bool in !part.isEmpty }
    return parts.joined(separator: " · ")
}

func featuredSymbol(for card: MediaCard) -> String {
    if card.section == "games" { return "gamecontroller" }
    if card.section == "series" { return "tv" }
    return "film"
}

/// Small caption over a shelf title, derived from the group id the engine sends.
func featuredKicker(for group: CuratedGroup, gamesOnly: Bool) -> String {
    let id = group.id
    if id.hasPrefix("seasonal-") { return tr("Сезон", "Season") }
    if id.hasPrefix("genre-") { return tr("Жанр", "Genre") }
    if id.hasPrefix("award") { return tr("Награды", "Awards") }
    if id == "fresh" { return tr("Новое", "New") }
    if id == "top-rated" { return tr("Рейтинг", "Rating") }
    if gamesOnly { return tr("Игры", "Games") }
    return tr("Подборка", "Collection")
}

/// A poster of a fixed size with rounded corners and a hairline.
struct FeaturedPoster: View {
    var address: String?
    var width: CGFloat
    var height: CGFloat
    var decodeWidth: Int
    var symbol: String
    var radius: CGFloat = KD.Radius.poster
    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        Artwork(address: address, width: decodeWidth, placeholderSymbol: symbol)
            .frame(width: width, height: height)
            .clipShape(shape)
            .overlay(shape.strokeBorder(Color.primary.opacity(0.08), lineWidth: 1))
    }
}

/// Rating marks (Кинопоиск, IMDb) followed by "2024 · Драма".
struct FeaturedMetaLine: View {
    var card: MediaCard
    var ratingLimit: Int = 2
    var body: some View {
        let text: String = featuredMetaText(card)
        HStack(spacing: KD.Space.s) {
            if ratingLimit > 0 {
                KDRatingRow(ratings: Array(card.ratings.prefix(ratingLimit)), valueColor: Color.primary)
            }
            if !text.isEmpty {
                Text(text).font(KDFont.meta).foregroundStyle(.secondary).lineLimit(1)
            }
        }
    }
}

/// English mode: fetches the English version of a few cards one after another (not all at once).
struct FeaturedLocalizeModifier: ViewModifier {
    @EnvironmentObject private var model: AppModel
    var cards: [MediaCard]
    @Binding var localized: [String: MediaCard]
    private var language: String { model.settings?.uiLanguage ?? "ru" }
    private var taskKey: String { language + ":" + cards.map { (card: MediaCard) -> String in card.id }.joined(separator: ",") }

    func body(content: Content) -> some View {
        content.task(id: taskKey) { await localize() }
    }

    private func localize() async {
        CatalogMemo.sync(language: language, revision: model.revision)
        if language != "en" {
            if !localized.isEmpty { localized = [:] }
            return
        }
        for card in cards where card.section != "games" {
            if localized[card.id] != nil { continue }
            let result: MediaCard? = await catalogLocalizedCard(card, bridge: model.bridge)
            if Task.isCancelled { return }
            if let result { localized[card.id] = result }
        }
    }
}

extension View {
    func featuredLocalized(_ cards: [MediaCard], into localized: Binding<[String: MediaCard]>) -> some View {
        modifier(FeaturedLocalizeModifier(cards: cards, localized: localized))
    }
}

// MARK: Digest "Главное сегодня"

struct FeaturedDigest: View {
    @EnvironmentObject private var model: AppModel
    var stories: [FeaturedDigestStory]
    @State private var localized: [String: MediaCard] = [:]
    @State private var width: CGFloat = 0

    private var columns: [GridItem] {
        let fitting: Int = width > 0 ? Int((width + KD.Space.m) / (300 + KD.Space.m)) : 3
        let count: Int = max(1, min(3, fitting))
        return Array(repeating: GridItem(.flexible(), spacing: KD.Space.m, alignment: .top), count: count)
    }

    private func shown(_ story: FeaturedDigestStory) -> MediaCard {
        catalogShownCard(story.card, local: localized[story.id], english: model.settings?.uiLanguage == "en")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            KDSectionHeader(title: tr("Главное сегодня", "Today's top stories"))
            LazyVGrid(columns: columns, alignment: .leading, spacing: KD.Space.m) {
                ForEach(stories) { story in
                    FeaturedDigestCard(card: shown(story), kicker: story.kicker)
                }
            }
        }
        .kdReadWidth($width)
        .padding(.horizontal, KD.gutter)
        .featuredLocalized(stories.map { (story: FeaturedDigestStory) -> MediaCard in story.card }, into: $localized)
    }
}

struct FeaturedDigestCard: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard
    var kicker: String

    var body: some View {
        Button { model.open(card) } label: { content }
            .buttonStyle(KDPosterButtonStyle(radius: KD.Radius.card))
            .help(card.title)
    }

    private var poster: some View {
        FeaturedPoster(address: card.poster, width: KD.Size.digestPoster, height: FeaturedLayout.digestPosterHeight, decodeWidth: 360, symbol: featuredSymbol(for: card))
    }

    private var text: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(kicker).font(KDFont.kicker).textCase(.uppercase).foregroundStyle(KDPalette.accent).lineLimit(1)
            Text(card.title).font(KDFont.cardTitle).lineLimit(2).multilineTextAlignment(.leading)
            if !card.description.isEmpty {
                Text(card.description).font(KDFont.secondary).foregroundStyle(.secondary).lineLimit(3).multilineTextAlignment(.leading)
            }
            Spacer(minLength: 0)
            FeaturedMetaLine(card: card, ratingLimit: 1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var content: some View {
        HStack(alignment: .top, spacing: KD.Space.m) {
            poster
            text
        }
        .padding(KD.Space.s)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .kdCard(radius: KD.Radius.card)
        .contentShape(RoundedRectangle(cornerRadius: KD.Radius.card, style: .continuous))
    }
}

// MARK: Lead rubric

/// One big story on the left and a numbered list 2...5 on the right; stacked when the column is narrow.
struct FeaturedLeadRubric: View {
    @EnvironmentObject private var model: AppModel
    var slice: FeaturedGroupSlice
    @State private var width: CGFloat = 0
    @State private var localized: [String: MediaCard] = [:]

    private var cards: [MediaCard] { Array(slice.cards.prefix(5)) }
    private var rest: [MediaCard] { Array(cards.dropFirst()) }
    private var isCompact: Bool { width > 0 && width < FeaturedLayout.leadSideBySideMinWidth }
    private func shown(_ card: MediaCard) -> MediaCard {
        catalogShownCard(card, local: localized[card.id], english: model.settings?.uiLanguage == "en")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            KDSectionHeader(
                title: slice.group.title,
                kicker: featuredKicker(for: slice.group, gamesOnly: slice.isGamesOnly),
                subtitle: slice.group.description,
                actionTitle: tr("Ещё", "More"),
                action: { model.openCollection(slice.group.id) }
            )
            stories
        }
        .kdReadWidth($width)
        .padding(.horizontal, KD.gutter)
        .featuredLocalized(cards, into: $localized)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(slice.group.title)
    }

    @ViewBuilder private var stories: some View {
        if let lead = cards.first {
            if isCompact {
                VStack(alignment: .leading, spacing: KD.Space.l) {
                    leadStory(lead)
                    if !rest.isEmpty { rankedList }
                }
            } else {
                HStack(alignment: .top, spacing: 28) {
                    leadStory(lead).frame(maxWidth: .infinity, alignment: .topLeading)
                    if !rest.isEmpty { rankedList.frame(maxWidth: .infinity, alignment: .topLeading) }
                }
            }
        }
    }

    private func leadStory(_ lead: MediaCard) -> some View {
        let item: MediaCard = shown(lead)
        return Button { model.open(item) } label: {
            HStack(alignment: .top, spacing: KD.Space.m) {
                FeaturedPoster(address: item.poster, width: FeaturedLayout.leadPosterWidth, height: FeaturedLayout.leadPosterHeight, decodeWidth: 480, symbol: featuredSymbol(for: item))
                VStack(alignment: .leading, spacing: 8) {
                    KDRankNumeral(rank: 1, size: 56)
                    Text(item.title).font(Font.title.bold()).lineLimit(3).multilineTextAlignment(.leading)
                    if !item.description.isEmpty {
                        Text(item.description).font(KDFont.secondary).foregroundStyle(.secondary).lineLimit(4).multilineTextAlignment(.leading)
                    }
                    FeaturedMetaLine(card: item)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(KDPosterButtonStyle(radius: KD.Radius.card))
        .help(item.title)
        .accessibilityLabel("№1, " + item.title)
    }

    private var rankedList: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(rest.enumerated()), id: \.element.id) { index, card in
                if index > 0 { Divider() }
                rankedRow(shown(card), rank: index + 2)
            }
        }
    }

    private func rankedRow(_ item: MediaCard, rank: Int) -> some View {
        Button { model.open(item) } label: {
            HStack(spacing: KD.Space.s) {
                KDRankNumeral(rank: rank, size: 28).frame(width: 30)
                FeaturedPoster(address: item.poster, width: FeaturedLayout.rowThumbWidth, height: FeaturedLayout.rowThumbHeight, decodeWidth: 120, symbol: featuredSymbol(for: item), radius: KD.Radius.chip)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.title).font(KDFont.rowTitle).lineLimit(2).multilineTextAlignment(.leading)
                    let meta: String = featuredMetaText(item)
                    if !meta.isEmpty { Text(meta).font(KDFont.meta).foregroundStyle(.secondary).lineLimit(1) }
                }
                Spacer(minLength: 0)
            }
            .padding(.vertical, KD.Space.xs)
            .contentShape(Rectangle())
        }
        .buttonStyle(KDPosterButtonStyle(radius: KD.Radius.thumb))
        .help(item.title)
        .accessibilityLabel("№" + String(rank) + ", " + item.title)
    }
}

// MARK: Ordinary shelf

struct FeaturedShelfSection: View {
    @EnvironmentObject private var model: AppModel
    var slice: FeaturedGroupSlice
    private var wide: Bool { slice.isGamesOnly }
    private var cardWidth: CGFloat { wide ? KD.Size.wideCardWidth : KD.Size.posterWidth }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            KDSectionHeader(
                title: slice.group.title,
                kicker: featuredKicker(for: slice.group, gamesOnly: wide),
                subtitle: slice.group.description,
                actionTitle: tr("Ещё", "More"),
                action: { model.openCollection(slice.group.id) }
            )
            .padding(.horizontal, KD.gutter)
            KDShelf(slice.cards) { card in
                PosterCard(card: card, wide: wide).frame(width: cardWidth)
            }
            .featuredClippedShelf()
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(slice.group.title)
    }
}

// MARK: Ranking with tabs

enum FeaturedRankKind: String, CaseIterable, Identifiable {
    case movies, series, games
    var id: String { rawValue }
    var title: String {
        switch self {
        case .movies: return tr("Фильмы", "Movies")
        case .series: return tr("Сериалы", "Series")
        case .games: return tr("Игры", "Games")
        }
    }
}

/// State of the ranking that must outlive the view: the page is a LazyVStack and discards a row that scrolls away.
struct FeaturedRankingState {
    /// The full chart (films, series, games); nil until the first load succeeds.
    var full: [MediaCard]?
    var kind: FeaturedRankKind = .movies
    /// The catalog revision `full` was loaded for.
    var loadedRevision: Int?
}

struct FeaturedRankedCard: Identifiable {
    var rank: Int
    var card: MediaCard
    var id: String { card.id }
}

/// The best-rated chart: the hub only carries ten titles, so the full 30 (films, series, games) are loaded once and split into tabs.
struct FeaturedRanking: View {
    @EnvironmentObject private var model: AppModel
    var group: CuratedGroup
    @Binding var state: FeaturedRankingState

    private var source: [MediaCard] { model.visible(state.full ?? group.items) }

    private func entries(for tab: FeaturedRankKind) -> [FeaturedRankedCard] {
        let matching: [MediaCard] = source.filter { (card: MediaCard) -> Bool in card.section == tab.rawValue }
        var rows: [FeaturedRankedCard] = []
        for (index, card) in matching.prefix(10).enumerated() { rows.append(FeaturedRankedCard(rank: index + 1, card: card)) }
        return rows
    }

    private var availableKinds: [FeaturedRankKind] {
        FeaturedRankKind.allCases.filter { (tab: FeaturedRankKind) -> Bool in !entries(for: tab).isEmpty }
    }

    private var activeKind: FeaturedRankKind {
        if availableKinds.contains(state.kind) { return state.kind }
        return availableKinds.first ?? state.kind
    }

    private var kindBinding: Binding<FeaturedRankKind> {
        Binding<FeaturedRankKind>(get: { activeKind }, set: { (value: FeaturedRankKind) in state.kind = value })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            KDSectionHeader(
                title: group.title,
                kicker: tr("Рейтинг", "Rating"),
                subtitle: group.description,
                actionTitle: tr("Ещё", "More"),
                action: { model.openCollection(group.id) }
            )
            .padding(.horizontal, KD.gutter)
            if availableKinds.count > 1 { tabs.padding(.horizontal, KD.gutter) }
            chart
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(group.title)
        .task(id: model.revision) { await loadFull() }
    }

    private var tabs: some View {
        Picker(tr("Тип рейтинга", "Ranking kind"), selection: kindBinding) {
            ForEach(availableKinds) { tab in Text(tab.title).tag(tab) }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .frame(maxWidth: 360)
        .accessibilityLabel(tr("Тип рейтинга", "Ranking kind"))
    }

    private var chart: some View {
        let tab: FeaturedRankKind = activeKind
        let wide: Bool = tab == .games
        let cardWidth: CGFloat = wide ? KD.Size.wideCardWidth : KD.Size.posterWidth
        return KDShelf(entries(for: tab)) { entry in
            HStack(alignment: .bottom, spacing: -14) {
                KDRankNumeral(rank: entry.rank, size: FeaturedLayout.rankNumeralSize).padding(.bottom, FeaturedLayout.rankNumeralLift)
                PosterCard(card: entry.card, wide: wide).frame(width: cardWidth)
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel("№" + String(entry.rank) + ", " + entry.card.title)
        }
        .featuredClippedShelf()
        .id(tab.rawValue)
    }

    private func loadFull() async {
        let revision: Int = model.revision
        if state.full != nil && state.loadedRevision == revision { return }
        let result: CatalogResult? = try? await model.bridge.call("featured", ["id": .string(group.id)], as: CatalogResult.self)
        if Task.isCancelled { return }
        if let result, !result.items.isEmpty {
            state.full = result.items
            state.loadedRevision = revision
        }
    }
}
