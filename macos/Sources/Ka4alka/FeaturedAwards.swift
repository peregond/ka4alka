import SwiftUI

/// Reply of the engine's `awards` call. `years` lists every ceremony year, newest first.
struct AwardPage: Decodable { var years: [Int]; var items: [MediaCard]; var winners: [MediaCard]?; var nominees: [MediaCard]? }

/// A collection page opened from "Ещё": award pages by year, or a plain list of titles (Steam shelves are paged).
struct CollectionView: View {
    var id: String
    var body: some View {
        if id.hasPrefix("award-") {
            FeaturedAwardsView(collectionID: id)
        } else {
            FeaturedCollectionPage(id: id)
        }
    }
}

/// The same title can be listed for several categories; a grid needs it once.
func featuredUnique(_ cards: [MediaCard]) -> [MediaCard] {
    var seen = Set<String>()
    var rows: [MediaCard] = []
    for card in cards where !seen.contains(card.id) {
        seen.insert(card.id)
        rows.append(card)
    }
    return rows
}

// MARK: Awards by year

struct FeaturedAwardsView: View {
    @EnvironmentObject private var model: AppModel
    var collectionID: String
    @State private var years: [Int] = []
    @State private var yearsLoaded = false
    @State private var yearsError: String?
    /// 0 means "all years". Kept in `model.awardYears` too, so Back returns to the same year.
    @State private var year = 0
    @State private var attempt = 0
    /// Loaded pages by catalog revision and year. The "all years" list is lazy and discards sections that scroll away; the cache keeps their content.
    @State private var pages: [String: AwardPage] = [:]
    /// The chip at the centre of the year strip.
    @State private var stripID: Int?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// Restarts the loading task when the collection, a retry or a catalog refresh changes.
    private var taskKey: String {
        let revision: String = String(model.revision)
        let tries: String = String(attempt)
        return "\(collectionID)|\(tries)|\(revision)"
    }

    private var awardID: String { String(collectionID.dropFirst(6)) }
    private var group: CuratedGroup? { model.curated.first { (value: CuratedGroup) -> Bool in value.id == collectionID } }
    private var title: String { group?.title ?? tr("Награды", "Awards") }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: KD.Space.l) {
                KDPageHeader(title, subtitle: group?.description)
                content
            }
            .padding(.horizontal, KD.gutter)
            .padding(.vertical, KD.gutter)
            .frame(maxWidth: KD.Width.page, alignment: .leading)
            .frame(maxWidth: .infinity)
        }
        .onChange(of: year) { _, next in model.awardYears[collectionID] = next }
        .task(id: taskKey) { await loadYears() }
    }

    @ViewBuilder private var content: some View {
        if let message = yearsError {
            KDEmptyState(
                title: tr("Не удалось загрузить награды", "Could not load the awards"),
                systemImage: "trophy",
                message: message,
                actionTitle: tr("Повторить", "Retry"),
                action: { model.error = nil; attempt += 1 }
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else if !yearsLoaded {
            KDPosterGridSkeleton(wide: false, count: 12)
        } else if years.isEmpty {
            KDEmptyState(
                title: tr("Данных о наградах пока нет", "No award data yet"),
                systemImage: "trophy",
                message: tr("Список появится после обновления каталога.", "The list appears after the catalog is refreshed.")
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else {
            yearStrip
            sections
        }
    }

    @ViewBuilder private var sections: some View {
        if year == 0 {
            LazyVStack(alignment: .leading, spacing: KD.Space.xl) {
                ForEach(years, id: \.self) { value in
                    FeaturedYearSection(awardID: awardID, year: value, solo: false, page: pageBinding(for: value))
                }
            }
        } else {
            FeaturedYearSection(awardID: awardID, year: year, solo: true, page: pageBinding(for: year)).id(year)
        }
    }

    private func pageKey(_ value: Int) -> String { String(model.revision) + "|" + String(value) }

    private func pageBinding(for value: Int) -> Binding<AwardPage?> {
        let key: String = pageKey(value)
        return Binding<AwardPage?>(
            get: { pages[key] },
            set: { (next: AwardPage?) in pages[key] = next }
        )
    }

    // MARK: Year strip

    private var yearIndex: Int? { years.firstIndex(of: year) }
    private var canGoNewer: Bool { if let index = yearIndex { return index > 0 }; return false }
    private var canGoOlder: Bool { if let index = yearIndex { return index < years.count - 1 }; return false }

    private func step(_ delta: Int) {
        guard let index = yearIndex else { return }
        let target = index + delta
        if target >= 0 && target < years.count { year = years[target] }
    }

    private var yearStrip: some View {
        HStack(spacing: KD.Space.xs) {
            KDIconButton(systemImage: "chevron.backward", label: tr("Следующий год", "Next year"), action: { step(-1) })
                .disabled(!canGoNewer)
            ScrollView(.horizontal) {
                HStack(spacing: KD.Space.xs) {
                    chip(0, title: tr("Все годы", "All years")).id(0)
                    ForEach(years, id: \.self) { value in
                        chip(value, title: String(value)).id(value)
                    }
                }
                .scrollTargetLayout()
                .padding(.vertical, 2)
            }
            .scrollIndicators(.hidden)
            .scrollPosition(id: $stripID, anchor: .center)
            .onAppear { stripID = year }
            .onChange(of: year) { _, next in followYear(next) }
            KDIconButton(systemImage: "chevron.forward", label: tr("Предыдущий год", "Previous year"), action: { step(1) })
                .disabled(!canGoOlder)
        }
    }

    /// Centres the chip of `target` in the strip. When the strip already reports it as its position, the position is reset first so the jump happens again.
    private func followYear(_ target: Int) {
        let animation: Animation? = reduceMotion ? nil : Animation.easeInOut(duration: 0.25)
        if stripID == target {
            stripID = nil
            DispatchQueue.main.async {
                withAnimation(animation) { stripID = target }
            }
        } else {
            withAnimation(animation) { stripID = target }
        }
    }

    private func chip(_ value: Int, title: String) -> some View {
        let selected: Bool = year == value
        let traits: AccessibilityTraits = selected ? AccessibilityTraits.isSelected : AccessibilityTraits()
        return Button { year = value } label: { KDChipLabel(text: title, isActive: selected) }
            .controlSize(.small)
            .buttonStyle(KDSecondaryButtonStyle())
            .accessibilityAddTraits(traits)
    }

    /// A call with a year that matches nothing returns only the list of years, which is all this page needs first.
    private func loadYears() async {
        yearsError = nil
        let currentPrefix: String = String(model.revision) + "|"
        pages = pages.filter { (entry: (key: String, value: AwardPage)) -> Bool in entry.key.hasPrefix(currentPrefix) }
        do {
            let result = try await model.bridge.call("awards", ["award": .string(awardID), "year": .number(-1)], as: AwardPage.self)
            if Task.isCancelled { return }
            let sorted: [Int] = result.years.sorted(by: >)
            years = sorted
            let newest: Int = sorted.first ?? 0
            let wanted: Int = yearsLoaded ? year : (model.awardYears[collectionID] ?? newest)
            year = (wanted == 0 || sorted.contains(wanted)) ? wanted : newest
            yearsLoaded = true
        } catch {
            if Task.isCancelled { return }
            yearsError = model.message(for: error) ?? tr("Не удалось загрузить награды.", "Could not load the awards.")
        }
    }
}

/// Winners and nominees of one ceremony year. Loads when it scrolls into view.
struct FeaturedYearSection: View {
    @EnvironmentObject private var model: AppModel
    var awardID: String
    var year: Int
    /// True when it is the only year on the page: an empty year then explains itself.
    var solo: Bool
    /// Owned by the page, so a section that scrolls out of the lazy list keeps what it loaded.
    @Binding var page: AwardPage?
    @State private var failure: String?
    @State private var attempt = 0
    private var taskKey: String {
        let revision: String = String(model.revision)
        let tries: String = String(attempt)
        return "\(awardID)|\(year)|\(tries)|\(revision)"
    }

    private var winners: [MediaCard] { featuredUnique(model.visible(page?.winners ?? [])) }
    private var nominees: [MediaCard] { featuredUnique(model.visible(page?.nominees ?? [])) }
    private var plain: [MediaCard] {
        guard let page else { return [] }
        if page.winners != nil || page.nominees != nil { return [] }
        return featuredUnique(model.visible(page.items))
    }
    private var hasContent: Bool { !winners.isEmpty || !nominees.isEmpty || !plain.isEmpty }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            sectionContent
        }
        .task(id: taskKey) { await load() }
    }

    private var heading: some View {
        Text(String(year)).font(KDFont.pageTitle).accessibilityAddTraits(.isHeader)
    }

    @ViewBuilder private var sectionContent: some View {
        if let message = failure {
            heading
            KDBanner(message: message, actionTitle: tr("Повторить", "Retry"), onAction: { model.error = nil; attempt += 1 })
        } else if page == nil {
            heading
            KDPosterGridSkeleton(wide: false, count: 6)
        } else if hasContent {
            heading
            if !winners.isEmpty { FeaturedAwardGrid(cards: winners, winner: true) }
            if !nominees.isEmpty { FeaturedAwardGrid(cards: nominees, winner: false) }
            if !plain.isEmpty { PosterGrid(items: plain) }
        } else if solo {
            KDEmptyState(
                title: tr("За этот год ничего нет", "Nothing for this year"),
                systemImage: "trophy",
                message: tr("Победители и номинанты появятся, когда они будут в каталоге.", "Winners and nominees appear once they are in the catalog.")
            )
            .frame(maxWidth: .infinity, minHeight: 280)
        }
    }

    private func load() async {
        if page != nil { return }
        failure = nil
        do {
            let result = try await model.bridge.call("awards", ["award": .string(awardID), "year": .number(Double(year))], as: AwardPage.self)
            if Task.isCancelled { return }
            page = result
        } catch {
            if Task.isCancelled { return }
            failure = model.message(for: error) ?? tr("Не удалось загрузить награды.", "Could not load the awards.")
        }
    }
}

/// A poster grid whose cards carry a "Победитель" or "Номинант" pill.
struct FeaturedAwardGrid: View {
    var cards: [MediaCard]
    var winner: Bool

    var body: some View {
        LazyVGrid(columns: KD.posterColumns(wide: false), alignment: .leading, spacing: KD.posterRowSpacing) {
            ForEach(cards) { card in
                VStack(alignment: .leading, spacing: 6) {
                    pill
                    PosterCard(card: card)
                }
            }
        }
    }

    @ViewBuilder private var pill: some View {
        if winner {
            KDStatusPill(tr("Победитель", "Winner"), tone: .accent, systemImage: "trophy.fill")
        } else {
            KDStatusPill(tr("Номинант", "Nominee"))
        }
    }
}

// MARK: Plain collections

struct FeaturedCollectionPage: View {
    @EnvironmentObject private var model: AppModel
    var id: String
    @State private var attempt = 0
    @State private var failed = false
    @State private var heading: String?
    @State private var scrollID: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let topID = "collection-top"
    /// The page lives in the model, so Back returns to the page that was open.
    private var collectionPage: Int { model.collectionPages[id] ?? 1 }
    private var taskKey: String {
        let page: String = String(collectionPage)
        let revision: String = String(model.revision)
        let tries: String = String(attempt)
        return "\(id)|\(page)|\(tries)|\(revision)"
    }

    private var isShelf: Bool { id.hasPrefix("steam-shelf:") }
    private var group: CuratedGroup? { model.curated.first { (value: CuratedGroup) -> Bool in value.id == id } }
    private var title: String {
        if let heading, !heading.isEmpty { return heading }
        if let listTitle = model.list?.title, !listTitle.isEmpty { return listTitle }
        return group?.title ?? tr("Подборка", "Collection")
    }
    private var summary: String {
        if let text = model.list?.description, !text.isEmpty { return text }
        return group?.description ?? ""
    }
    private func isWide(_ list: CatalogResult) -> Bool {
        id != "top-rated" && list.items.allSatisfy { (card: MediaCard) -> Bool in card.section == "games" }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: KD.Space.l) {
                KDPageHeader(title, subtitle: summary)
                    .padding(.top, KD.gutter)
                    .id(Self.topID)
                listContent
                if isShelf, let list = model.list { pager(list) }
            }
            .scrollTargetLayout()
            .padding(.horizontal, KD.gutter)
            .padding(.bottom, KD.gutter)
            .frame(maxWidth: KD.Width.page, alignment: .leading)
            .frame(maxWidth: .infinity)
        }
        .scrollPosition(id: $scrollID, anchor: .top)
        .onChange(of: collectionPage) { _, _ in jumpToTop() }
        .task(id: taskKey) { await load() }
    }

    private func setPage(_ next: Int) {
        model.collectionPages[id] = max(1, next)
    }

    /// Scrolls to the title. When the page already reports it as its position, the position is reset first so the jump happens again.
    private func jumpToTop() {
        let target: String = Self.topID
        let animation: Animation? = reduceMotion ? nil : Animation.easeInOut(duration: 0.25)
        if scrollID == target {
            scrollID = nil
            DispatchQueue.main.async {
                withAnimation(animation) { scrollID = target }
            }
        } else {
            withAnimation(animation) { scrollID = target }
        }
    }

    @ViewBuilder private var listContent: some View {
        if let list = model.list {
            if list.items.isEmpty {
                KDEmptyState(
                    title: tr("Подборка пока недоступна", "Collection unavailable"),
                    systemImage: "rectangle.stack",
                    message: tr("Попробуй позже.", "Try again later.")
                )
                .frame(maxWidth: .infinity, minHeight: 320)
            } else {
                PosterGrid(items: list.items, wide: isWide(list))
            }
        } else if failed {
            KDEmptyState(
                title: tr("Не удалось загрузить подборку", "Could not load the collection"),
                systemImage: "rectangle.stack.badge.minus",
                message: nil,
                actionTitle: tr("Повторить", "Retry"),
                action: { model.error = nil; failed = false; attempt += 1 }
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else {
            KDPosterGridSkeleton(wide: isShelf)
        }
    }

    private func pager(_ list: CatalogResult) -> some View {
        HStack(spacing: KD.Space.m) {
            Spacer()
            Button { setPage(collectionPage - 1) } label: {
                Label(tr("Назад", "Previous"), systemImage: "chevron.backward")
            }
            .buttonStyle(KDSecondaryButtonStyle())
            .keyboardShortcut(.leftArrow, modifiers: .command)
            .disabled(collectionPage <= 1)
            Text(tr("Страница ", "Page ") + String(collectionPage)).font(KDFont.meta).monospacedDigit()
            Button { setPage(collectionPage + 1) } label: {
                Label(tr("Далее", "Next"), systemImage: "chevron.forward")
            }
            .buttonStyle(KDSecondaryButtonStyle())
            .keyboardShortcut(.rightArrow, modifiers: .command)
            .disabled(!list.hasNext)
            Spacer()
        }
        .padding(.vertical, KD.Space.s)
    }

    private func load() async {
        failed = false
        await model.loadCollection(id, page: collectionPage)
        if Task.isCancelled { return }
        if let listTitle = model.list?.title, !listTitle.isEmpty { heading = listTitle }
        failed = model.list == nil
    }
}
