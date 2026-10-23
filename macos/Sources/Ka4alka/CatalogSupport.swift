import SwiftUI

// Helpers shared by CatalogView.swift and HeroBanner.swift. Every top-level name starts with "Catalog" / "catalog".

/// Lets at most four `localizedCard` requests run at once, so a long English-mode grid does not flood the engine.
actor CatalogGate {
    static let shared = CatalogGate(limit: 4)
    private var free: Int
    private var waiting: [CheckedContinuation<Void, Never>] = []
    init(limit: Int) { free = limit }
    func enter() async {
        if free > 0 { free -= 1; return }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            waiting.append(continuation)
        }
    }
    func leave() {
        if waiting.isEmpty { free += 1 } else { waiting.removeFirst().resume() }
    }
}

/// What the catalog remembers between view lifetimes: lazy grids and the banner recreate their views, and without this
/// the Russian card or the blurred poster would flash before the English card or the backdrop arrives again.
@MainActor enum CatalogMemo {
    static var localized: [String: MediaCard] = [:]
    static var backdrops: [String: String] = [:]
    private static var stamp = ""

    /// Forgets everything when the language or the catalog revision (refresh, content settings) differs from the last call.
    static func sync(language: String, revision: Int) {
        let next: String = language + "|" + String(revision)
        if next == stamp { return }
        stamp = next
        localized = [:]
        backdrops = [:]
    }
}

/// The card to draw: in English mode the English copy (own or remembered) when there is one, otherwise the card itself.
@MainActor func catalogShownCard(_ card: MediaCard, local: MediaCard?, english: Bool) -> MediaCard {
    if !english { return card }
    return local ?? CatalogMemo.localized[card.id] ?? card
}

/// The English version of a card, or nil when the request failed or was cancelled.
@MainActor func catalogLocalizedCard(_ card: MediaCard, bridge: EngineBridge) async -> MediaCard? {
    if let known = CatalogMemo.localized[card.id] { return known }
    await CatalogGate.shared.enter()
    if Task.isCancelled {
        await CatalogGate.shared.leave()
        return nil
    }
    let result = try? await bridge.call("localizedCard", ["id": .string(card.id)], as: MediaCard.self)
    await CatalogGate.shared.leave()
    if Task.isCancelled { return nil }
    if let result { CatalogMemo.localized[card.id] = result }
    return result
}

/// Labels a poster for VoiceOver and adds "Downloaded" once the title is in the finished downloads.
/// It observes the queue, so the label follows a download that completes while the card is on screen.
struct CatalogPosterLabel: ViewModifier {
    @ObservedObject var store: TransferStore
    var cardID: String
    var text: String

    private var downloaded: Bool {
        store.items.contains { (item: Transfer) -> Bool in item.mediaId == cardID && item.complete }
    }

    func body(content: Content) -> some View {
        let label: String = downloaded ? text + ", " + tr("Скачано", "Downloaded") : text
        return content.accessibilityLabel(label)
    }
}

/// Stable landscape geometry shared by loading placeholders and real slides.
enum CatalogHeroMetrics {
    static func height(forWidth width: CGFloat, viewportHeight: CGFloat? = nil) -> CGFloat {
        let landscape = min(560, max(380, width * 0.48))
        guard let viewportHeight else { return landscape }
        // Keep the actions in view in a short window; the scroll container already
        // subtracts its pinned filters. Reserve the catalog's top spacing as well.
        return min(landscape, max(280, viewportHeight - 40))
    }
    static func backdropWidth(forWidth width: CGFloat, lite: Bool) -> Int {
        if lite { return 640 }
        return width > 1200 ? 1920 : 1280
    }
}

/// "Драма, Триллер" -> the first `limit` genres; the store label "Windows" is not a genre.
func catalogGenreLine(_ genre: String, limit: Int) -> String {
    let parts: [String] = genre.split(separator: ",")
        .map { (part: Substring) -> String in part.trimmingCharacters(in: .whitespaces) }
        .filter { (part: String) -> Bool in !part.isEmpty && part != "Windows" }
    return parts.prefix(limit).map { tr($0) }.joined(separator: ", ")
}

/// "1 название", "2 названия", "5 названий" (and the English forms).
func catalogTitleCount(_ count: Int) -> String {
    if KDFormat.isEnglish { return count == 1 ? "1 title" : String(count) + " titles" }
    let lastDigit: Int = count % 10
    let lastTwo: Int = count % 100
    let word: String
    if lastDigit == 1 && lastTwo != 11 { word = "название" }
    else if lastDigit >= 2 && lastDigit <= 4 && !(lastTwo >= 12 && lastTwo <= 14) { word = "названия" }
    else { word = "названий" }
    return String(count) + " " + word
}

/// A decade of older years for the Year filter ("2000-е": 2009 ... 2000).
struct CatalogYearGroup: Identifiable {
    var decade: Int
    var years: [Int]
    var id: Int { decade }
    var title: String { tr(String(decade) + "-е", String(decade) + "s") }

    /// Years from 1950 up to (not including) `recentStart`, newest decade first.
    static func groups(before recentStart: Int) -> [CatalogYearGroup] {
        var rows: [CatalogYearGroup] = []
        var decade: Int = ((recentStart - 1) / 10) * 10
        while decade >= 1950 {
            var years: [Int] = []
            var year: Int = decade + 9
            while year >= decade {
                if year < recentStart { years.append(year) }
                year -= 1
            }
            if !years.isEmpty { rows.append(CatalogYearGroup(decade: decade, years: years)) }
            decade -= 10
        }
        return rows
    }
}

struct CatalogSortOption: Identifiable {
    var key: String
    var label: String
    var id: String { key }
}

/// A removable chip under the filter row ("Жанр · Драма").
struct CatalogAppliedChip: Identifiable {
    var id: String
    var text: String
    var clear: () -> Void
}

/// Download state drawn over a poster: a check when the title is downloaded, a thin progress bar while it downloads,
/// and a small heart when it is saved. It observes the queue only, so progress ticks redraw this view and nothing else.
struct CatalogPosterStatus: View {
    @ObservedObject var store: TransferStore
    var cardID: String
    var saved: Bool

    private var related: [Transfer] { store.items.filter { $0.mediaId == cardID } }
    private var downloaded: Bool { related.contains { $0.complete } }
    private var activeProgress: Double? {
        if downloaded { return nil }
        guard let active = related.first(where: { $0.isActive }) else { return nil }
        return min(100, max(0, active.progress)) / 100
    }

    var body: some View {
        Color.clear
            .allowsHitTesting(false)
            .overlay(alignment: .topTrailing) { badges.padding(8) }
            .overlay(alignment: .bottom) { progressBar }
            .accessibilityHidden(true)
    }

    private var badges: some View {
        VStack(alignment: .trailing, spacing: 4) {
            if downloaded { checkBadge }
            if saved { heartBadge }
        }
    }

    private var checkBadge: some View {
        Image(systemName: "checkmark")
            .font(.system(size: 12, weight: .bold))
            .foregroundStyle(KDPalette.accentInk)
            .frame(width: 24, height: 24)
            .background(KDPalette.accentFill, in: Circle())
    }

    private var heartBadge: some View {
        Image(systemName: "heart.fill")
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(KDPalette.like)
            .frame(width: 22, height: 22)
            .background(Color.black.opacity(0.6), in: Circle())
    }

    @ViewBuilder private var progressBar: some View {
        if let fraction = activeProgress {
            GeometryReader { proxy in
                ZStack(alignment: .leading) {
                    Rectangle().fill(Color.black.opacity(0.45))
                    Rectangle().fill(KDPalette.accentFill).frame(width: proxy.size.width * CGFloat(fraction))
                }
            }
            .frame(height: 4)
        }
    }
}
