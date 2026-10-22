import AppKit
import SwiftUI

// MARK: Detail page

struct DetailView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    var card: MediaCard
    @State private var detailLoading = true
    @State private var franchiseExpanded = false
    @State private var scrollTarget: String?
    private static let releasesAnchor = "detail-releases"

    /// The loaded detail, but only when it belongs to this card.
    private var detail: DetailResult? { model.detail?.item.id == card.id ? model.detail : nil }
    private var item: MediaCard { detail?.item ?? card }
    private var isGame: Bool { card.section == "games" }
    private var lite: Bool { model.settings?.liteMode ?? false }
    private var loadKey: String { card.id + "|" + (model.settings?.uiLanguage ?? "ru") }

    var body: some View {
        ScrollView {
            page
        }
        .scrollPosition(id: $scrollTarget, anchor: .top)
        .task(id: loadKey) { await loadDetail() }
        .task(id: loadKey) { await model.loadReleases(card) }
    }

    private var page: some View {
        VStack(alignment: .leading, spacing: KD.Space.l) {
            DetailHero(card: card, detail: detail, onChooseRelease: { scrollToReleases() }, onOtherParts: card.section == "movies" ? { franchiseExpanded.toggle() } : nil)
            descriptionBlock
            gameBlock
            peopleBlock
            if franchiseExpanded { DetailFranchiseSection(card: item).id(card.id) }
            awardsBlock
            VStack(alignment: .leading, spacing: 0) {
                DetailReleasesSection(transfers: model.transfers, card: card).id(card.id)
            }
            .id(DetailView.releasesAnchor)
        }
        .scrollTargetLayout()
        .frame(maxWidth: KD.Width.page, alignment: .leading)
        .padding(KD.gutter)
        .frame(maxWidth: .infinity)
    }

    // MARK: Loading

    private func loadDetail() async {
        detailLoading = true
        await model.loadDetail(card)
        if !Task.isCancelled { detailLoading = false }
    }

    /// Retry runs both requests, so a failed page and an empty release list recover together.
    private func retryAll() {
        Task { @MainActor in await loadDetail() }
        Task { @MainActor in await model.loadReleases(card) }
    }

    private func scrollToReleases() {
        let target: String = DetailView.releasesAnchor
        let animation: Animation? = reduceMotion ? nil : Animation.easeInOut(duration: 0.3)
        if scrollTarget == target {
            // The page already reports the releases as its top item; reset the position so the jump happens again.
            scrollTarget = nil
            DispatchQueue.main.async {
                withAnimation(animation) { scrollTarget = target }
            }
        } else {
            withAnimation(animation) { scrollTarget = target }
        }
    }

    // MARK: Blocks

    @ViewBuilder private var descriptionBlock: some View {
        if !item.description.isEmpty {
            DetailExpandableText(text: item.description).id(card.id)
        } else if detail != nil {
            Text(tr("Описание пока недоступно.", "No description yet."))
                .font(KDFont.secondary)
                .foregroundStyle(.secondary)
        } else if detailLoading {
            DetailSkeletonLines()
        } else {
            detailFailed
        }
    }

    private var detailFailed: some View {
        HStack(spacing: KD.Space.s) {
            Label(tr("Не удалось загрузить описание.", "Could not load the description."), systemImage: "exclamationmark.triangle")
                .font(KDFont.secondary)
                .foregroundStyle(KDPalette.warning)
            Button(tr("Повторить", "Retry")) { retryAll() }
                .buttonStyle(KDSecondaryButtonStyle())
                .controlSize(.small)
        }
    }

    @ViewBuilder private var gameBlock: some View {
        if let game = detail?.game {
            VStack(alignment: .leading, spacing: KD.Space.m) {
                Label(tr("Раздачи для Windows не запускаются в macOS без совместимого ПО.", "Windows releases do not run directly on macOS."), systemImage: "desktopcomputer")
                    .font(KDFont.secondary)
                    .foregroundStyle(.secondary)
                GameFactsCard(game: game, item: item)
                if !game.gallery.isEmpty && !lite { GameGalleryView(shots: game.gallery).id(card.id) }
                if !game.requirements.isEmpty { DetailRequirementsView(text: game.requirements) }
            }
        }
    }

    @ViewBuilder private var peopleBlock: some View {
        if let people = detail?.people, !people.isEmpty {
            DetailCastSection(people: people).id(card.id)
        } else if detail != nil && !isGame {
            DetailCastEmpty(onRetry: { Task { @MainActor in await loadDetail() } })
        }
    }

    @ViewBuilder private var awardsBlock: some View {
        if let awards = detail?.awards, !awards.isEmpty {
            DetailAwardsSection(awards: awards)
        }
    }

}

// MARK: Hero banner

struct DetailHero: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard
    var detail: DetailResult?
    var onChooseRelease: () -> Void
    var onOtherParts: (() -> Void)? = nil
    @State private var width: CGFloat = 0

    private var item: MediaCard { detail?.item ?? card }
    private var isGame: Bool { card.section == "games" }
    private var lite: Bool { model.settings?.liteMode ?? false }
    private var compact: Bool { width > 0 && width < KD.Width.compactContent }

    private var originalTitle: String? {
        guard let original = detail?.originalTitle, !original.isEmpty else { return nil }
        if original.lowercased() == item.title.lowercased() { return nil }
        return original
    }

    private var metaLine: String {
        var parts: [String] = []
        let genres: String = (detail?.genres ?? "").isEmpty ? item.genre : (detail?.genres ?? "")
        if !genres.isEmpty { parts.append(genres) }
        if let country = detail?.country, !country.isEmpty { parts.append(country) }
        if item.year > 0 { parts.append(String(item.year)) }
        return parts.joined(separator: " · ")
    }

    private var badges: [String] {
        var result: [String] = []
        for height in detail?.resolutions ?? [] {
            let title: String = DetailHero.resolutionTitle(height)
            if !result.contains(title) { result.append(title) }
        }
        if result.isEmpty, let quality = item.quality, DetailReleaseText.meaningful(quality) { result.append(quality) }
        return result
    }

    static func resolutionTitle(_ height: Int) -> String {
        if height >= 2160 { return "4K" }
        if height >= 1080 { return "Full HD" }
        if height >= 720 { return "HD Ready" }
        return String(height) + "p"
    }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: KD.Radius.hero, style: .continuous)
        content
            .background { layers }
            .clipShape(shape)
            .kdReadWidth($width)
            .settingsLayoutProbe("detail.banner")
    }

    // MARK: Layers

    private var layers: some View {
        ZStack {
            KDBannerBase()
            backdrop
            KDBannerScrim()
        }
    }

    /// The artwork fades in by itself (and not at all under Reduce Motion). Lite mode skips it.
    @ViewBuilder private var backdrop: some View {
        if !lite, let address = detail?.backdrop, !address.isEmpty {
            GeometryReader { proxy in
                Artwork(address: address, width: proxy.size.width > 1200 ? 1920 : 1280)
                    .frame(width: proxy.size.width, height: proxy.size.height)
                    .kdBackdropExtension()
            }
        }
    }

    // MARK: Content

    private var content: some View {
        HStack(alignment: .bottom, spacing: compact ? KD.Space.m : KD.Space.l) {
            posterView
            textColumn
        }
        .padding(compact ? KD.Space.m : KD.Space.l)
        .frame(maxWidth: .infinity, minHeight: KD.Size.heroHeight, alignment: .bottomLeading)
        .kdOnBanner()
    }

    @ViewBuilder private var posterView: some View {
        if isGame {
            Artwork(address: item.poster, width: 640, placeholderSymbol: "gamecontroller")
                .aspectRatio(460.0 / 215.0, contentMode: .fit)
                .frame(width: compact ? 220 : 320)
                .clipShape(RoundedRectangle(cornerRadius: KD.Radius.poster, style: .continuous))
                .settingsLayoutProbe("detail.hero.poster", group: "detail.banner")
        } else {
            Artwork(address: item.poster, width: 400, placeholderSymbol: card.section == "series" ? "tv" : "film")
                .aspectRatio(2.0 / 3.0, contentMode: .fit)
                .frame(width: compact ? 100 : 135)
                .clipShape(RoundedRectangle(cornerRadius: KD.Radius.poster, style: .continuous))
        }
    }

    private var textColumn: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            Text(item.title)
                .font(compact ? KDFont.displayCompact : KDFont.display)
                .lineLimit(3)
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityAddTraits(.isHeader)
            if let original = originalTitle {
                Text(original)
                    .font(KDFont.secondary)
                    .foregroundStyle(Color.white.opacity(0.78))
                    .lineLimit(2)
            }
            if !item.ratings.isEmpty { KDRatingRow(ratings: item.ratings) }
            if !metaLine.isEmpty {
                Text(metaLine)
                    .font(KDFont.meta)
                    .foregroundStyle(Color.white.opacity(0.85))
                    .lineLimit(2)
            }
            let tags = badges
            if !tags.isEmpty {
                KDFlowLayout(spacing: 6, lineSpacing: 6) {
                    ForEach(tags, id: \.self) { text in
                        KDPosterBadge(text: text)
                    }
                }
            }
            DetailActionRow(item: item, isGame: isGame, onChooseRelease: onChooseRelease, onOtherParts: onOtherParts)
                .padding(.top, KD.Space.xs)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// MARK: Hero actions

struct DetailActionRow: View {
    @EnvironmentObject private var model: AppModel
    var item: MediaCard
    var isGame: Bool
    var onChooseRelease: () -> Void
    var onOtherParts: (() -> Void)? = nil
    @State private var shareURL: URL?

    private var saved: Bool { model.isSaved(item) }

    var body: some View {
        ViewThatFits(in: .horizontal) {
            actions(labelled: true)
            actions(labelled: false)
            stackedActions
        }
        .settingsLayoutProbe("detail.actions")
        .task(id: item.id) { await loadShareLink() }
    }

    private func actions(labelled: Bool) -> some View {
        HStack(spacing: KD.Space.s) {
            DetailPrimaryAction(transfers: model.transfers, card: item, onChooseRelease: onChooseRelease)
            secondaryButtons(labelled: labelled)
        }
    }

    /// Narrow banners: the primary action gets its own line and the icon buttons sit under it.
    private var stackedActions: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            DetailPrimaryAction(transfers: model.transfers, card: item, onChooseRelease: onChooseRelease)
            HStack(spacing: KD.Space.s) {
                secondaryButtons(labelled: false)
            }
        }
    }

    @ViewBuilder private func secondaryButtons(labelled: Bool) -> some View {
        heroButton(saved ? tr("Сохранено", "Saved") : tr("Сохранить", "Save"), saved ? "heart.fill" : "heart", labelled: labelled) {
            Task { @MainActor in await model.save(item) }
        }
        heroButton(tr("Не интересно", "Not interested"), "eye.slash", labelled: labelled) {
            Task { @MainActor in
                let hidden = await model.hide(item)
                if hidden { model.back() }
            }
        }
        if let onOtherParts { heroButton(tr("Другие части", "Other parts"), "square.stack", labelled: labelled, action: onOtherParts) }
        shareControl(labelled: labelled)
    }

    @ViewBuilder private func heroButton(_ title: String, _ systemImage: String, labelled: Bool, action: @escaping () -> Void) -> some View {
        if labelled {
            Button(action: action) { Label(title, systemImage: systemImage) }
                .buttonStyle(KDOverlayButtonStyle())
        } else {
            Button(action: action) { Image(systemName: systemImage) }
                .buttonStyle(KDOverlayButtonStyle(circular: true))
                .help(title)
                .accessibilityLabel(title)
        }
    }

    /// The engine has no share link for games, so there is no Share control for them.
    @ViewBuilder private func shareControl(labelled: Bool) -> some View {
        if !isGame, let url = shareURL {
            if labelled {
                ShareLink(item: url, subject: Text(item.title)) {
                    Label(tr("Поделиться", "Share"), systemImage: "square.and.arrow.up")
                }
                .buttonStyle(KDOverlayButtonStyle())
            } else {
                ShareLink(item: url, subject: Text(item.title)) {
                    Image(systemName: "square.and.arrow.up")
                }
                .buttonStyle(KDOverlayButtonStyle(circular: true))
                .help(tr("Поделиться", "Share"))
                .accessibilityLabel(tr("Поделиться", "Share"))
            }
        }
    }

    private func loadShareLink() async {
        shareURL = nil
        if isGame { return }
        do {
            let result = try await model.bridge.call("share", ["id": .string(item.id)])
            let text = result["url"].string
            if !Task.isCancelled, !text.isEmpty, let url = URL(string: text), url.scheme != nil { shareURL = url }
        } catch {
            // No link means no Share control; the rest of the page is unaffected.
            shareURL = nil
        }
    }
}

/// The one state-aware primary action: show the file, follow the download, take the recommended release, or pick one.
struct DetailPrimaryAction: View {
    @EnvironmentObject private var model: AppModel
    @ObservedObject var transfers: TransferStore
    var card: MediaCard
    var onChooseRelease: () -> Void
    @State private var adding = false

    private var own: [Transfer] { transfers.items.filter { $0.mediaId == card.id } }
    private var finished: Transfer? { own.first(where: { $0.complete && !$0.missing }) }
    private var inQueue: Transfer? {
        if let running = own.first(where: { $0.isActive }) { return running }
        return own.first(where: { !$0.complete })
    }
    private var recommended: ReleaseRow? {
        model.recommendedRelease(for: card)
    }

    var body: some View {
        if let done = finished {
            showButton(done)
        } else if let queued = inQueue {
            progressButton(queued)
        } else if let row = recommended {
            downloadButton(row)
        } else {
            chooseButton
        }
    }

    private func showButton(_ transfer: Transfer) -> some View {
        Button {
            if transfer.folder.isEmpty {
                model.select(.downloads)
            } else {
                NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: transfer.folder)])
            }
        } label: {
            Label(tr("Показать в папке", "Show in folder"), systemImage: "folder")
        }
        .buttonStyle(KDPrimaryButtonStyle())
        .controlSize(.large)
    }

    private func progressButton(_ transfer: Transfer) -> some View {
        let verb: String = transfer.isActive ? tr("Качается", "Downloading") : (transfer.paused ? tr("Пауза", "Paused") : tr("В загрузках", "In downloads"))
        let text = verb + " · " + KDFormat.percent(transfer.progress)
        return Button {
            model.select(.downloads)
        } label: {
            Label(text, systemImage: "arrow.down.circle")
        }
        .buttonStyle(KDPrimaryButtonStyle())
        .controlSize(.large)
        .help(tr("Открыть загрузки", "Open Downloads"))
    }

    private func downloadTitle(_ row: ReleaseRow) -> String {
        var parts: [String] = [tr("Скачать", "Download")]
        let quality = DetailReleaseText.qualityText(row, isGame: card.section == "games")
        if quality != "—" { parts.append(quality) }
        if let size = row.size { parts.append(KDFormat.bytes(size)) }
        return parts.joined(separator: " · ")
    }

    private func downloadButton(_ row: ReleaseRow) -> some View {
        Button {
            start(row)
        } label: {
            if adding {
                ProgressView().controlSize(.small)
            } else {
                Label(downloadTitle(row), systemImage: "arrow.down").lineLimit(1)
            }
        }
        .buttonStyle(KDPrimaryButtonStyle())
        .controlSize(.large)
        .disabled(adding)
    }

    private var chooseButton: some View {
        Button {
            onChooseRelease()
        } label: {
            Label(tr("Выбрать раздачу", "Choose a release"), systemImage: "list.bullet")
        }
        .buttonStyle(KDPrimaryButtonStyle())
        .controlSize(.large)
    }

    private func start(_ row: ReleaseRow) {
        if adding { return }
        adding = true
        Task { @MainActor in
            _ = await model.add(row, card: card)
            adding = false
        }
    }
}

// MARK: Person page

struct PersonView: View {
    @EnvironmentObject var model: AppModel
    var person: Person
    @State private var loading = true

    private var profile: PersonResult? { model.personResult(for: person) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: KD.Space.l) {
                header
                filmography
            }
            .frame(maxWidth: KD.Width.page, alignment: .leading)
            .padding(KD.gutter)
            .frame(maxWidth: .infinity)
        }
        .task(id: person.id) { await load() }
    }

    private func load() async {
        loading = true
        await model.loadPerson(person)
        if !Task.isCancelled { loading = false }
    }

    private func retry() {
        Task { @MainActor in await load() }
    }

    // MARK: Header

    private var header: some View {
        HStack(alignment: .top, spacing: KD.Space.l) {
            Artwork(address: profile?.person.photoUrl ?? person.photoUrl, width: 400, placeholderSymbol: "person.crop.rectangle")
                .frame(width: 170, height: 215)
                .clipShape(RoundedRectangle(cornerRadius: KD.Radius.card, style: .continuous))
            VStack(alignment: .leading, spacing: KD.Space.s) {
                Text(person.name)
                    .font(KDFont.pageTitle)
                    .accessibilityAddTraits(.isHeader)
                Text(detailRoleTitle(person.role))
                    .font(KDFont.secondary)
                    .foregroundStyle(.secondary)
                biography
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    @ViewBuilder private var biography: some View {
        if let profile {
            if profile.description.isEmpty {
                Text(tr("Биография пока недоступна.", "No biography yet."))
                    .font(KDFont.secondary)
                    .foregroundStyle(.secondary)
            } else {
                DetailExpandableText(text: profile.description, lines: 6, expandTitle: tr("Подробнее", "More")).id(person.id)
            }
            if let url = sourceLink(profile.sourceUrl) {
                Link(tr("Источник биографии", "Biography source"), destination: url)
                    .font(.callout)
                    .foregroundStyle(KDPalette.accent)
            }
        } else if loading {
            DetailSkeletonLines()
        } else {
            HStack(spacing: KD.Space.s) {
                Label(tr("Не удалось загрузить страницу.", "Could not load the page."), systemImage: "exclamationmark.triangle")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.warning)
                Button(tr("Повторить", "Retry")) { retry() }
                    .buttonStyle(KDSecondaryButtonStyle())
                    .controlSize(.small)
            }
        }
    }

    /// Only the biography sources the engine uses are linked.
    private func sourceLink(_ text: String?) -> URL? {
        guard let url = detailSecureURL(text), let host = url.host else { return nil }
        if host == "ru.wikipedia.org" && url.path.hasPrefix("/wiki/") { return url }
        if host == "kino-teatr.ua" || host == "www.kino-teatr.ua" { return url }
        return nil
    }

    // MARK: Filmography

    private func newestFirst(_ items: [MediaCard]) -> [MediaCard] {
        let indexed: [(offset: Int, element: MediaCard)] = Array(items.enumerated())
        let ordered = indexed.sorted { (left: (offset: Int, element: MediaCard), right: (offset: Int, element: MediaCard)) -> Bool in
            if left.element.year != right.element.year { return left.element.year > right.element.year }
            return left.offset < right.offset
        }
        return ordered.map { $0.element }
    }

    private func ratingValue(_ card: MediaCard) -> Double {
        let text = card.rating.replacingOccurrences(of: ",", with: ".")
        return Double(text) ?? 0
    }

    private func bestRated(_ items: [MediaCard]) -> [MediaCard] {
        let rated: [MediaCard] = items.filter { ratingValue($0) > 0 }
        let ordered = rated.sorted { ratingValue($0) > ratingValue($1) }
        return Array(ordered.prefix(10))
    }

    @ViewBuilder private var filmography: some View {
        if let profile {
            let films: [MediaCard] = newestFirst(model.visible(profile.items))
            if films.isEmpty {
                emptyFilmography
            } else {
                filmographyContent(films)
            }
        } else if loading {
            KDShelfSkeleton()
        }
    }

    private var emptyFilmography: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            KDSectionHeader(title: tr("Фильмография", "Filmography"))
            Text(tr("Подтверждённые фильмы и сериалы пока недоступны.", "No confirmed films or series yet."))
                .font(KDFont.secondary)
                .foregroundStyle(.secondary)
            Button(tr("Повторить", "Retry")) { retry() }
                .buttonStyle(KDSecondaryButtonStyle())
                .controlSize(.small)
        }
    }

    private func filmographyContent(_ films: [MediaCard]) -> some View {
        let best: [MediaCard] = films.count > 3 ? bestRated(films) : []
        return VStack(alignment: .leading, spacing: KD.Space.l) {
            if best.count >= 3 {
                CollapsibleFilmGrid(title: tr("Лучшие по рейтингу", "Top rated"), items: best)
            }
            VStack(alignment: .leading, spacing: KD.Space.s) {
                KDSectionHeader(
                    title: tr("Фильмография", "Filmography"),
                    subtitle: tr("Фильмов и сериалов: \(films.count)", "Films and series: \(films.count)")
                )
                PosterGrid(items: films)
            }
        }
    }
}
