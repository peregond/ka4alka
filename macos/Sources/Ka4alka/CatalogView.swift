import SwiftUI

private enum CatalogPosterFocus: Hashable { case card, save, hide }

// MARK: Poster card

struct PosterCard: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard; var wide = false; var rank: Int? = nil
    @State private var localized: MediaCard?
    @State private var hovered = false
    @FocusState private var focus: CatalogPosterFocus?

    private var english: Bool { model.settings?.uiLanguage == "en" }
    private var item: MediaCard { catalogShownCard(card, local: localized, english: english) }
    private var wideArt: Bool { wide || card.section == "games" }
    private var saved: Bool { model.isSaved(card) }
    private var showsActions: Bool { hovered || focus != nil }
    private var posterShape: RoundedRectangle { RoundedRectangle(cornerRadius: KD.Radius.poster, style: .continuous) }
    private var placeholderSymbol: String {
        switch card.section {
        case "series": return "tv"
        case "games": return "gamecontroller"
        default: return "film"
        }
    }
    private var saveTitle: String { saved ? tr("Удалить из сохранённого", "Remove from Saved") : tr("Сохранить", "Save") }
    private var hideTitle: String { tr("Не интересно", "Not interested") }

    var body: some View {
        ZStack(alignment: .top) {
            openButton
            actionsLayer
        }
        .onHover { hovered = $0 }
        .contextMenu { menuItems }
        .help(item.title)
        .task(id: card.id + (model.settings?.uiLanguage ?? "ru")) { await localizeIfNeeded() }
    }

    // MARK: Pieces

    private var openButton: some View {
        Button { model.open(item) } label: { cardLabel }
            .buttonStyle(KDPosterButtonStyle())
            .focused($focus, equals: .card)
            .modifier(CatalogPosterLabel(store: model.transfers, cardID: card.id, text: accessibilityText))
            .accessibilityAction(named: Text(saveTitle)) { Task { await toggleSave() } }
            .accessibilityAction(named: Text(hideTitle)) { Task { await model.hide(card) } }
    }

    private var cardLabel: some View {
        VStack(alignment: .leading, spacing: 7) {
            posterArt
            Text(item.title)
                .font(KDFont.rowTitle)
                .lineLimit(2)
                .multilineTextAlignment(.leading)
                .frame(maxWidth: .infinity, minHeight: KD.titleBlockHeight, alignment: .topLeading)
            metaLine
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentShape(Rectangle())
    }

    private var posterArt: some View {
        Color.clear
            .aspectRatio(KD.posterAspect(wide: wideArt), contentMode: .fit)
            .overlay { Artwork(address: item.poster, width: wideArt ? 720 : 640, placeholderSymbol: placeholderSymbol) }
            .overlay { CatalogPosterStatus(store: model.transfers, cardID: card.id, saved: saved) }
            .overlay(alignment: .topLeading) { topLeadingBadge.padding(8) }
            .overlay(alignment: .bottomLeading) { bottomLeadingMark.padding(8) }
            .overlay(alignment: .bottomTrailing) { rankMark.padding(8) }
            .clipShape(posterShape)
            .overlay(posterShape.strokeBorder(Color.primary.opacity(0.08), lineWidth: 1))
    }

    @ViewBuilder private var topLeadingBadge: some View {
        if let store = item.store, store.soon == true {
            KDPosterBadge(text: store.soonLabel ?? tr("Скоро", "Soon"))
        } else if let quality = item.quality, !quality.isEmpty {
            KDPosterBadge(text: tr(quality))
        }
    }

    @ViewBuilder private var bottomLeadingMark: some View {
        if let store = item.store {
            if store.free || !store.price.isEmpty { priceChip(store) }
        } else if let first = item.ratings.first {
            KDRatingChip(source: first.source, value: first.value)
        }
    }

    @ViewBuilder private var rankMark: some View {
        if let rank, !showsActions {
            KDRankNumeral(rank: rank, size: 40)
                .environment(\.colorScheme, .dark)
        }
    }

    private func priceChip(_ store: StoreCard) -> some View {
        HStack(spacing: 0) {
            if store.discount > 0 {
                Text("−" + String(store.discount) + "%")
                    .font(Font.caption.weight(.bold))
                    .foregroundStyle(KDPalette.accentInk)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 4)
                    .background(KDPalette.accentFill)
            }
            Text(store.free ? tr("Бесплатно", "Free") : store.price)
                .font(Font.caption.weight(.bold))
                .foregroundStyle(Color.white)
                .padding(.horizontal, 7)
                .padding(.vertical, 4)
        }
        .background(Color.black.opacity(0.72))
        .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
    }

    private var metaLine: some View {
        HStack(spacing: 6) {
            reviewMark
            Text(metaText)
                .font(KDFont.meta)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
    }

    private var metaText: String {
        var parts: [String] = []
        if item.year > 0 { parts.append(String(item.year)) }
        let genre = catalogGenreLine(item.genre, limit: 2)
        if !genre.isEmpty { parts.append(genre) }
        return parts.joined(separator: " · ")
    }

    @ViewBuilder private var reviewMark: some View {
        if let store = item.store, store.reviewPercent > 0 {
            HStack(spacing: 3) {
                Image(systemName: "hand.thumbsup.fill").font(.system(size: 11))
                Text(String(store.reviewPercent) + "%").font(KDFont.meta.monospacedDigit())
            }
            .foregroundStyle(reviewTone(store).foreground)
            .help(store.reviewLabel ?? "")
        }
    }

    private func reviewTone(_ store: StoreCard) -> KDTone {
        switch store.reviewTone ?? "" {
        case "positive": return .success
        case "mixed": return .warning
        case "negative": return .danger
        default: return .neutral
        }
    }

    // MARK: Hover and focus actions

    private var actionsLayer: some View {
        Color.clear
            .aspectRatio(KD.posterAspect(wide: wideArt), contentMode: .fit)
            .allowsHitTesting(false)
            .overlay(alignment: .bottomTrailing) {
                if showsActions { actionButtons }
            }
    }

    private var actionButtons: some View {
        KDGlassGroup(spacing: 8) {
            HStack(spacing: 8) {
                Button { Task { await toggleSave() } } label: {
                    Image(systemName: saved ? "heart.fill" : "heart")
                }
                .buttonStyle(KDOverlayButtonStyle(circular: true))
                .focused($focus, equals: .save)
                .help(saveTitle)
                .accessibilityLabel(saveTitle)
                Button { Task { await model.hide(card) } } label: {
                    Image(systemName: "eye.slash")
                }
                .buttonStyle(KDOverlayButtonStyle(circular: true))
                .focused($focus, equals: .hide)
                .help(hideTitle)
                .accessibilityLabel(hideTitle)
            }
        }
        .padding(8)
    }

    @ViewBuilder private var menuItems: some View {
        Button(saveTitle) { Task { await toggleSave() } }
        Button(hideTitle) { Task { await model.hide(card) } }
    }

    // MARK: Behaviour

    private var accessibilityText: String {
        var parts: [String] = [item.title]
        if item.year > 0 { parts.append(String(item.year)) }
        let genre = catalogGenreLine(item.genre, limit: 2)
        if !genre.isEmpty { parts.append(genre) }
        for rating in item.ratings { parts.append(rating.source.spokenName + " " + rating.value) }
        if let quality = item.quality, !quality.isEmpty { parts.append(tr(quality)) }
        if let store = item.store {
            if store.free { parts.append(tr("Бесплатно", "Free")) } else if !store.price.isEmpty { parts.append(store.price) }
            if store.reviewPercent > 0 { parts.append(store.reviewLabel ?? (String(store.reviewPercent) + "%")) }
        }
        if saved { parts.append(tr("Сохранено", "Saved")) }
        return parts.joined(separator: ", ")
    }

    private func toggleSave() async {
        let wasSaved = model.isSaved(card)
        await model.save(card)
        if wasSaved { model.toast = tr("Убрано из сохранённого", "Removed from Saved") }
    }

    private func localizeIfNeeded() async {
        guard english, card.section != "games" else { return }
        CatalogMemo.sync(language: "en", revision: model.revision)
        let result = await catalogLocalizedCard(card, bridge: model.bridge)
        if !Task.isCancelled { localized = result }
    }
}

// MARK: Grid

struct PosterGrid: View {
    @EnvironmentObject var model: AppModel
    var items: [MediaCard]; var wide = false
    var body: some View {
        LazyVGrid(columns: KD.posterColumns(wide: wide), alignment: .leading, spacing: KD.posterRowSpacing) {
            ForEach(model.visible(items)) { card in PosterCard(card: card, wide: wide).id(card.id) }
        }.scrollTargetLayout()
    }
}

// MARK: Catalog page

struct CatalogView: View {
    @EnvironmentObject private var model: AppModel
    var saved = false
    /// The scroll position by item id (cards, or `topID`). It mirrors `model.browseAnchor`, so Back returns to the same place.
    @State private var scrollID: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// True once the load for the current `loadID` has finished (successfully or not).
    @State private var settled = false
    private static let topID = "catalog-top"

    private var showsSpotlight: Bool {
        !saved && (model.section == .movies || model.section == .series)
            && model.page == 1 && model.submittedQuery.isEmpty && !model.filter.active
    }
    /// The filters stay on screen while a filter or a search is active; the page title always scrolls with the content.
    private var pinsHeader: Bool { !saved && (model.filter.active || !model.submittedQuery.isEmpty) }
    private var showsPageHeader: Bool { saved || !model.submittedQuery.isEmpty || model.section == .games }
    private var loadID: String { saved ? "saved-" + String(model.revision) : model.requestKey }
    private var showsPager: Bool { !saved && model.submittedQuery.isEmpty }

    var body: some View {
        pageBody.task(id: loadID) {
            settled = false
            if saved { await model.loadSaved() } else { await model.loadCatalog() }
            if !Task.isCancelled { settled = true }
        }
    }

    @ViewBuilder private var pageBody: some View {
        if pinsHeader {
            scroller.kdPinnedHeader { pinnedHeader }
        } else {
            scroller
        }
    }

    private var scroller: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Color.clear.frame(height: KD.Space.m).id(Self.topID)
                scrollContent
            }
            .scrollTargetLayout()
            .padding(.horizontal, KD.gutter)
            .padding(.bottom, KD.gutter)
        }
        .scrollPosition(id: $scrollID, anchor: .top)
        .onAppear { if scrollID == nil { scrollID = model.browseAnchor } }
        .onChange(of: scrollID) { _, next in model.browseAnchor = next }
        .onChange(of: loadID) { _, _ in jumpToTop() }
    }

    /// Scrolls to the top. When the page already reports the top as its position, the position is reset first so the jump happens again.
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

    private var pinnedHeader: some View {
        FilterBar()
        .padding(.horizontal, KD.gutter)
        .padding(.vertical, KD.Space.s)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder private var pageHeader: some View {
        if saved {
            KDPageHeader(tr("Сохранённое", "Saved"), subtitle: tr("Кино, к которому хочется вернуться.", "Films you want to come back to."))
        } else if !model.submittedQuery.isEmpty {
            KDPageHeader(tr("Результаты поиска", "Search results"), subtitle: String(model.submittedQuery.prefix(80)))
        } else if model.section == .games {
            KDPageHeader(model.section.title, subtitle: tr("Каталог игр для Windows. Загрузки можно передать на ПК по локальной сети.", "Windows game catalog. Send downloads to a PC on your local network."))
        }
    }

    @ViewBuilder private var scrollContent: some View {
        if showsPageHeader { pageHeader }
        heroBlock
        if !saved, !pinsHeader { FilterBar() }
        resultsBlock
    }

    // MARK: Hero

    @ViewBuilder private var heroBlock: some View {
        if showsSpotlight {
            if let result = model.list {
                let shown = model.visible(result.items)
                if !shown.isEmpty {
                    CatalogSpotlightView(items: Array(shown.prefix(5)))
                        .id(model.section.rawValue)
                        .settingsLayoutProbe("catalog.banner." + model.section.rawValue)
                    Text(model.section == .series ? tr("Все сериалы", "All series") : tr("Все фильмы", "All movies"))
                        .font(KDFont.section)
                        .accessibilityAddTraits(.isHeader)
                }
            } else if !settled {
                HeroPlaceholder()
            }
        }
    }

    // MARK: Results

    @ViewBuilder private var resultsBlock: some View {
        if let result = model.list {
            resultsContent(result)
        } else if settled, !model.loading {
            unavailable
        } else {
            loadingBlock
        }
    }

    @ViewBuilder private func resultsContent(_ result: CatalogResult) -> some View {
        let shown = model.visible(result.items)
        if saved {
            savedBody(shown)
        } else {
            if result.offline == true { offlineBanner }
            if shown.isEmpty {
                emptyState(hiddenOnly: !result.items.isEmpty)
            } else {
                PosterGrid(items: shown, wide: model.section == .games)
            }
            if showsPager, !shown.isEmpty || model.page > 1 || result.hasNext { pager(hasNext: result.hasNext) }
        }
    }

    private var offlineBanner: some View {
        KDBanner(
            message: tr("Показаны сохранённые результаты. Часть источников недоступна.", "Showing cached results. Some sources are unavailable."),
            tone: .warning,
            systemImage: "wifi.slash"
        )
    }

    private var unavailable: some View {
        KDEmptyState(
            title: tr("Каталог пока недоступен", "Catalog unavailable"),
            systemImage: "wifi.slash",
            message: model.error ?? tr("Не удалось загрузить каталог", "Could not load the catalog"),
            actionTitle: tr("Повторить", "Retry"),
            action: { model.revision += 1 }
        )
        .frame(maxWidth: .infinity, minHeight: 320)
    }

    @ViewBuilder private var loadingBlock: some View {
        SkeletonGrid(wide: model.section == .games && !saved)
        if showsPager, model.page > 1 { pager(hasNext: false) }
    }

    @ViewBuilder private func emptyState(hiddenOnly: Bool) -> some View {
        if hiddenOnly {
            KDEmptyState(
                title: tr("Ничего не найдено", "Nothing found"),
                systemImage: "eye.slash",
                message: tr("Скрытые названия можно вернуть в настройках.", "Hidden titles can be restored in Settings.")
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else if !model.submittedQuery.isEmpty {
            searchEmptyState
        } else if model.filter.active {
            KDEmptyState(
                title: tr("Ничего не найдено", "Nothing found"),
                systemImage: "line.3.horizontal.decrease.circle",
                message: tr("Попробуй изменить или сбросить фильтры.", "Try changing or resetting the filters."),
                actionTitle: tr("Сбросить фильтры", "Reset filters"),
                action: { model.filter = CatalogFilter() }
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else {
            KDEmptyState(
                title: tr("Здесь пока пусто", "Nothing here yet"),
                systemImage: "film",
                message: tr("Каталог ещё обновляется. Загляни чуть позже.", "The catalog is still updating. Check back in a moment.")
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        }
    }

    @ViewBuilder private var searchEmptyState: some View {
        if model.filter.active {
            KDEmptyState(
                title: tr("Ничего не найдено", "Nothing found"),
                systemImage: "magnifyingglass",
                message: model.submittedQuery,
                actionTitle: tr("Сбросить фильтры", "Reset filters"),
                action: { model.filter = CatalogFilter() }
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else {
            KDEmptyState(
                title: tr("Ничего не найдено", "Nothing found"),
                systemImage: "magnifyingglass",
                message: model.submittedQuery
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        }
    }

    // MARK: Pager

    private func pager(hasNext: Bool) -> some View {
        HStack(spacing: KD.Space.m) {
            Button(tr("Предыдущая", "Previous")) { goToPage(model.page - 1) }
                .keyboardShortcut(.leftArrow, modifiers: .command)
                .disabled(model.page <= 1 || model.loading)
            Text(tr("Страница " + String(model.page), "Page " + String(model.page)))
                .font(KDFont.meta.monospacedDigit())
                .foregroundStyle(.secondary)
            Button(tr("Далее", "Next")) { goToPage(model.page + 1) }
                .keyboardShortcut(.rightArrow, modifiers: .command)
                .disabled(!hasNext || model.loading)
        }
        .buttonStyle(KDSecondaryButtonStyle())
        .frame(maxWidth: .infinity)
        .padding(.vertical, KD.Space.m)
    }

    private func goToPage(_ page: Int) {
        model.page = max(1, page)
        model.browseAnchor = nil
    }

    // MARK: Saved

    @ViewBuilder private func savedBody(_ shown: [MediaCard]) -> some View {
        if shown.isEmpty {
            KDEmptyState(
                title: tr("Пока ничего не сохранено", "Nothing saved yet"),
                systemImage: "heart",
                message: tr("Нажми на сердечко на карточке, и название появится здесь.", "Tap the heart on a card and it will appear here.")
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else {
            savedControls(shown)
            savedGroups(savedRows(shown))
        }
    }

    private func savedGroupKey(_ card: MediaCard) -> String {
        switch card.section {
        case "games": return "games"
        case "series": return "series"
        default: return "movies"
        }
    }

    private func savedRating(_ card: MediaCard) -> Double {
        var best: Double = -1
        for rating in card.ratings {
            let text = rating.value.replacingOccurrences(of: ",", with: ".")
            if let number = Double(text), number > best { best = number }
        }
        return best
    }

    private func savedRows(_ shown: [MediaCard]) -> [MediaCard] {
        var rows: [MediaCard] = shown
        if !model.savedKind.isEmpty { rows = shown.filter { savedGroupKey($0) == model.savedKind } }
        if model.savedSort == "rating" {
            rows = rows.sorted { savedRating($0) > savedRating($1) }
        } else {
            rows = Array(rows.reversed())
        }
        return rows
    }

    private func savedCount(_ shown: [MediaCard], kind: String) -> Int {
        if kind.isEmpty { return shown.count }
        return shown.filter { savedGroupKey($0) == kind }.count
    }

    private func savedControls(_ shown: [MediaCard]) -> some View {
        KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
            Picker(tr("Показывать", "Show"), selection: $model.savedKind) {
                Text(tr("Все", "All") + " · " + String(savedCount(shown, kind: ""))).tag("")
                Text(tr("Фильмы", "Movies") + " · " + String(savedCount(shown, kind: "movies"))).tag("movies")
                Text(tr("Сериалы", "Series") + " · " + String(savedCount(shown, kind: "series"))).tag("series")
                Text(tr("Игры", "Games") + " · " + String(savedCount(shown, kind: "games"))).tag("games")
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(width: 400)
            .accessibilityLabel(tr("Показывать", "Show"))
            savedSortMenu
            Text(catalogTitleCount(shown.count))
                .font(KDFont.meta)
                .foregroundStyle(.secondary)
                .frame(minHeight: KD.Size.iconButton)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .settingsLayoutProbe("catalog.saved.controls")
    }

    private var savedSortLabel: String {
        model.savedSort == "rating" ? tr("По рейтингу", "Top rated") : tr("Сначала новые", "Newest first")
    }

    private var savedSortMenu: some View {
        KDChipMenu(title: savedSortLabel, value: model.savedSort == "rating" ? savedSortLabel : nil) {
            Picker(tr("Порядок", "Sort"), selection: $model.savedSort) {
                Text(tr("Сначала новые", "Newest first")).tag("date")
                Text(tr("По рейтингу", "Top rated")).tag("rating")
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
        .accessibilityLabel(tr("Порядок", "Sort"))
        .accessibilityValue(savedSortLabel)
    }

    @ViewBuilder private func savedGroups(_ rows: [MediaCard]) -> some View {
        if rows.isEmpty {
            KDEmptyState(
                title: tr("Здесь пока ничего нет", "Nothing here yet"),
                systemImage: "heart",
                message: tr("В этой категории нет сохранённых названий.", "There are no saved titles in this category.")
            )
            .frame(maxWidth: .infinity, minHeight: 240)
        } else if !model.savedKind.isEmpty {
            PosterGrid(items: rows, wide: model.savedKind == "games")
        } else {
            savedSection(key: "movies", title: tr("Фильмы", "Movies"), rows: rows)
            savedSection(key: "series", title: tr("Сериалы", "Series"), rows: rows)
            savedSection(key: "games", title: tr("Игры", "Games"), rows: rows)
        }
    }

    @ViewBuilder private func savedSection(key: String, title: String, rows: [MediaCard]) -> some View {
        let part = rows.filter { savedGroupKey($0) == key }
        if !part.isEmpty {
            VStack(alignment: .leading, spacing: KD.Space.s) {
                KDSectionHeader(title: title, subtitle: catalogTitleCount(part.count))
                PosterGrid(items: part, wide: key == "games")
            }
        }
    }
}

// MARK: Filters

struct FilterBar: View {
    @EnvironmentObject var model: AppModel
    private var isGames: Bool { model.section == .games }
    private var genreItems: [Choice] { isGames ? model.gameGenres : model.genres }
    private var showsKind: Bool { !model.submittedQuery.isEmpty && !isGames }
    private var showsReset: Bool { model.filter.active || !model.submittedQuery.isEmpty }
    private var currentYear: Int { Calendar.current.component(.year, from: Date()) }
    private var recentStart: Int { currentYear - 14 }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
                if showsKind { kindPicker }
                genreMenu
                if !isGames {
                    countryMenu
                    yearMenu
                    ratingMenu
                    qualityMenu
                }
                sortMenu
                if showsReset { resetButton }
            }
            appliedRow
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .settingsLayoutProbe("catalog.filters")
    }

    // MARK: Bindings

    private var genreBinding: Binding<String> {
        Binding(get: { model.filter.genre }, set: { model.filter.genre = $0 })
    }
    private var countryBinding: Binding<String> {
        Binding(get: { model.filter.country }, set: { model.filter.country = $0 })
    }
    private var yearBinding: Binding<Int> {
        Binding(get: { model.filter.year }, set: { model.filter.year = $0 })
    }
    private var ratingBinding: Binding<Int> {
        Binding(get: { model.filter.rating }, set: { model.filter.rating = $0 })
    }
    private var qualityBinding: Binding<String> {
        Binding(get: { model.filter.quality }, set: { model.filter.quality = $0 })
    }
    private var kindBinding: Binding<String> {
        Binding(get: { model.filter.kind }, set: { model.filter.kind = $0 })
    }
    private var sortBinding: Binding<String> {
        Binding(
            get: { currentSortKey },
            set: { (key: String) in model.filter.sort = (isGames && key == "popular") ? "date" : key }
        )
    }
    private var hidePoorBinding: Binding<Bool> {
        Binding(
            get: { model.settings?.hidePoorQuality ?? true },
            set: { (value: Bool) in Task { await model.set("hidePoorQuality", .bool(value)) } }
        )
    }

    // MARK: Menus

    private var kindPicker: some View {
        Picker(tr("Показывать", "Show"), selection: kindBinding) {
            Text(tr("Все", "All")).tag("")
            Text(tr("Фильмы", "Movies")).tag("movies")
            Text(tr("Сериалы", "Series")).tag("series")
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .frame(width: 260)
        .accessibilityLabel(tr("Показывать", "Show"))
    }

    private func choiceLabel(for key: String, in items: [Choice]) -> String? {
        if key.isEmpty { return nil }
        return items.first { $0.key == key }?.label
    }

    @ViewBuilder private func choiceRows(_ items: [Choice]) -> some View {
        Text(tr("Все", "All")).tag("")
        ForEach(items) { item in Text(item.label).tag(item.key) }
    }

    private var genreMenu: some View {
        KDChipMenu(title: tr("Жанр", "Genre"), value: choiceLabel(for: model.filter.genre, in: genreItems)) {
            Picker(tr("Жанр", "Genre"), selection: genreBinding) { choiceRows(genreItems) }
                .pickerStyle(.inline)
                .labelsHidden()
        }
    }

    private var countryMenu: some View {
        KDChipMenu(title: tr("Страна", "Country"), value: choiceLabel(for: model.filter.country, in: model.countries)) {
            Picker(tr("Страна", "Country"), selection: countryBinding) { choiceRows(model.countries) }
                .pickerStyle(.inline)
                .labelsHidden()
        }
    }

    private var yearMenu: some View {
        KDChipMenu(title: tr("Год", "Year"), value: model.filter.year == 0 ? nil : String(model.filter.year)) {
            Picker(tr("Год", "Year"), selection: yearBinding) {
                Text(tr("Любой год", "Any year")).tag(0)
                ForEach(recentYears, id: \.self) { year in Text(String(year)).tag(year) }
            }
            .pickerStyle(.inline)
            .labelsHidden()
            ForEach(CatalogYearGroup.groups(before: recentStart)) { group in
                Menu(group.title) {
                    Picker(group.title, selection: yearBinding) {
                        ForEach(group.years, id: \.self) { year in Text(String(year)).tag(year) }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }
            }
        }
    }

    private var recentYears: [Int] {
        var years: [Int] = []
        var year: Int = currentYear
        while year >= recentStart {
            years.append(year)
            year -= 1
        }
        return years
    }

    private var ratingMenu: some View {
        KDChipMenu(title: tr("Рейтинг", "Rating"), value: model.filter.rating == 0 ? nil : String(model.filter.rating) + "+") {
            Picker(tr("Рейтинг", "Rating"), selection: ratingBinding) {
                Text(tr("Любой рейтинг", "Any rating")).tag(0)
                ForEach(Array((1...9).reversed()), id: \.self) { value in Text(String(value) + "+").tag(value) }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
    }

    private var qualityMenu: some View {
        KDChipMenu(title: tr("Качество", "Quality"), value: model.filter.quality.isEmpty ? nil : model.filter.quality) {
            Picker(tr("Качество", "Quality"), selection: qualityBinding) {
                Text(tr("Любое качество", "Any quality")).tag("")
                Text("HD Ready").tag("HD Ready")
                Text("Full HD").tag("Full HD")
                Text("4K").tag("4K")
            }
            .pickerStyle(.inline)
            .labelsHidden()
            Divider()
            Toggle(tr("Без плохого качества", "Hide low quality"), isOn: hidePoorBinding)
        }
    }

    private var sortOptions: [CatalogSortOption] {
        if isGames {
            return [
                CatalogSortOption(key: "popular", label: tr("Популярное", "Popular")),
                CatalogSortOption(key: "new", label: tr("Новинки", "New")),
                CatalogSortOption(key: "specials", label: tr("Скидки", "On sale")),
                CatalogSortOption(key: "soon", label: tr("Скоро выйдут", "Coming soon"))
            ]
        }
        return [
            CatalogSortOption(key: "date", label: tr("Сначала новые", "Newest first")),
            CatalogSortOption(key: "rating", label: tr("По рейтингу", "Top rated"))
        ]
    }

    private var currentSortKey: String {
        if isGames && model.filter.sort == "date" { return "popular" }
        return model.filter.sort
    }

    private var currentSortLabel: String {
        sortOptions.first { $0.key == currentSortKey }?.label ?? tr("Порядок", "Sort")
    }

    private var isDefaultSort: Bool { model.filter.sort == "date" }

    private var sortMenu: some View {
        KDChipMenu(title: currentSortLabel, value: isDefaultSort ? nil : currentSortLabel) {
            Picker(tr("Порядок", "Sort"), selection: sortBinding) {
                ForEach(sortOptions) { option in Text(option.label).tag(option.key) }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
        .accessibilityLabel(tr("Порядок", "Sort"))
        .accessibilityValue(currentSortLabel)
    }

    // MARK: Reset and applied chips

    private var resetButton: some View {
        Button { resetAll() } label: {
            Text(tr("Сбросить всё", "Reset all"))
                .font(Font.callout.weight(.semibold))
                .foregroundStyle(KDPalette.accent)
                .padding(.horizontal, KD.Space.xs)
                .frame(minHeight: KD.Size.iconButton)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private func resetAll() {
        model.filter = CatalogFilter()
        model.query = ""
        model.submittedQuery = ""
        model.page = 1
        model.revision += 1
    }

    private var appliedChips: [CatalogAppliedChip] {
        var rows: [CatalogAppliedChip] = []
        if let text = choiceLabel(for: model.filter.genre, in: genreItems) {
            rows.append(CatalogAppliedChip(id: "genre", text: tr("Жанр", "Genre") + " · " + text, clear: { model.filter.genre = "" }))
        }
        if !isGames, let text = choiceLabel(for: model.filter.country, in: model.countries) {
            rows.append(CatalogAppliedChip(id: "country", text: tr("Страна", "Country") + " · " + text, clear: { model.filter.country = "" }))
        }
        if !isGames, model.filter.year != 0 {
            rows.append(CatalogAppliedChip(id: "year", text: tr("Год", "Year") + " · " + String(model.filter.year), clear: { model.filter.year = 0 }))
        }
        if !isGames, model.filter.rating != 0 {
            rows.append(CatalogAppliedChip(id: "rating", text: tr("Рейтинг", "Rating") + " · " + String(model.filter.rating) + "+", clear: { model.filter.rating = 0 }))
        }
        if !isGames, !model.filter.quality.isEmpty {
            rows.append(CatalogAppliedChip(id: "quality", text: tr("Качество", "Quality") + " · " + model.filter.quality, clear: { model.filter.quality = "" }))
        }
        return rows
    }

    @ViewBuilder private var appliedRow: some View {
        let chips = appliedChips
        if !chips.isEmpty {
            KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
                ForEach(chips) { chip in appliedButton(chip) }
            }
        }
    }

    private func appliedButton(_ chip: CatalogAppliedChip) -> some View {
        Button(action: chip.clear) {
            KDChipLabel(text: chip.text, systemImage: "xmark", isActive: true)
        }
        .buttonStyle(KDSecondaryButtonStyle())
        .help(tr("Убрать фильтр", "Remove filter"))
        .accessibilityLabel(tr("Убрать фильтр: ", "Remove filter: ") + chip.text)
    }
}

// MARK: Skeleton

struct SkeletonGrid: View {
    var wide = false
    var body: some View { KDPosterGridSkeleton(wide: wide) }
}
