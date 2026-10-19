import AppKit
import SwiftUI

/// A single visible hero uses the same bounded artwork cache as poster cards.
/// No timer preloads the remaining slides or redraws the catalog while scrolling.
struct CatalogSpotlightView: View {
    var items: [MediaCard]
    @State private var selectedID: String?
    private var selected: MediaCard? { items.first { $0.id == selectedID } ?? items.first }

    var body: some View {
        if let selected {
            VStack(spacing: CatalogHeroMetrics.pagerGap) {
                FeaturedHero(card: selected, eyebrow: eyebrow(for: selected))
                    .id(selected.id)
                if items.count > 1 { pager(selected: selected) }
            }
            .onKeyPress(keys: [.leftArrow, .rightArrow]) { (press: KeyPress) -> KeyPress.Result in
                if !press.modifiers.isEmpty { return .ignored }
                advance(press.key == .leftArrow ? -1 : 1)
                return .handled
            }
            .accessibilityIdentifier("catalog-spotlight")
        }
    }

    private func eyebrow(for card: MediaCard) -> String {
        if card.section == "games" { return tr("Стоит поиграть", "Worth playing") }
        if items.first?.id == card.id { return tr("В центре внимания", "In the spotlight") }
        return tr("Стоит посмотреть", "Worth watching")
    }

    private func pager(selected: MediaCard) -> some View {
        HStack(spacing: 0) {
            dots(selected: selected)
            Spacer(minLength: KD.Space.s)
            KDIconButton(systemImage: "chevron.backward", label: tr("Предыдущий баннер", "Previous banner")) { advance(-1) }
            KDIconButton(systemImage: "chevron.forward", label: tr("Следующий баннер", "Next banner")) { advance(1) }
        }
    }

    private func dots(selected: MediaCard) -> some View {
        HStack(spacing: 0) {
            ForEach(items) { item in dot(item, isSelected: item.id == selected.id) }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(tr("Баннеры", "Banners"))
    }

    private func dot(_ item: MediaCard, isSelected: Bool) -> some View {
        Button { selectedID = item.id } label: {
            Capsule()
                .fill(isSelected ? KDPalette.accent : Color.primary.opacity(0.35))
                .frame(width: isSelected ? 24 : 8, height: 8)
                .frame(minWidth: KD.Size.minHit, minHeight: KD.Size.minHit)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(item.title)
        .accessibilityLabel(item.title)
        .accessibilityAddTraits(isSelected ? AccessibilityTraits.isSelected : AccessibilityTraits())
    }

    private func advance(_ delta: Int) {
        guard !items.isEmpty else { return }
        let index = items.firstIndex { $0.id == selected?.id } ?? 0
        selectedID = items[(index + delta + items.count) % items.count].id
    }
}

/// The dark banner base with shimmering lines, drawn while the first page is loading.
struct HeroPlaceholder: View {
    @State private var width: CGFloat = 0
    var body: some View {
        KDHeroSkeleton(height: CatalogHeroMetrics.height(forWidth: width > 0 ? width : 900))
            .kdReadWidth($width)
    }
}

struct FeaturedHero: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard
    var eyebrow: String? = nil
    @State private var backdrop: String?
    @State private var localized: MediaCard?
    @State private var width: CGFloat = 0
    private var language: String { model.settings?.uiLanguage ?? "ru" }
    private var item: MediaCard { catalogShownCard(card, local: localized, english: language == "en") }
    /// The loaded address, or the one remembered from an earlier showing, so a banner flip does not restart from the blurred poster.
    private var shownBackdrop: String? { backdrop ?? CatalogMemo.backdrops[card.id] }
    private var lite: Bool { model.settings?.liteMode == true }
    private var requestID: String { card.id + ":" + language }
    private var heroHeight: CGFloat { CatalogHeroMetrics.height(forWidth: width > 0 ? width : 900) }
    private var saved: Bool { model.isSaved(card) }
    private var saveTitle: String { saved ? tr("Удалить из сохранённого", "Remove from Saved") : tr("Сохранить", "Save") }
    private var heroShape: RoundedRectangle { RoundedRectangle(cornerRadius: KD.Radius.hero, style: .continuous) }

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .bottomLeading) {
                openButton(size: geometry.size)
                actionRow
            }
            .kdOnBanner()
        }
        .frame(height: heroHeight)
        .kdReadWidth($width)
        .settingsLayoutProbe("catalog.hero")
        .task(id: requestID) {
            CatalogMemo.sync(language: language, revision: model.revision)
            if let known = CatalogMemo.backdrops[card.id] {
                backdrop = known
                return
            }
            backdrop = nil
            let result = try? await model.bridge.call("artwork", ["id": .string(card.id)])
            if !Task.isCancelled {
                let address = result?["url"].string ?? ""
                if !address.isEmpty { CatalogMemo.backdrops[card.id] = address }
                backdrop = address.isEmpty ? nil : address
            }
        }
        .task(id: "localized-" + requestID) {
            CatalogMemo.sync(language: language, revision: model.revision)
            localized = nil
            if language == "en", card.section != "games" {
                let result = await catalogLocalizedCard(card, bridge: model.bridge)
                if !Task.isCancelled { localized = result }
            }
        }
    }

    // MARK: Pieces

    private func openButton(size: CGSize) -> some View {
        Button { model.open(item) } label: { bannerFace(size: size) }
            .buttonStyle(KDPosterButtonStyle(radius: KD.Radius.hero))
            .accessibilityLabel(accessibilityTitle)
            .accessibilityIdentifier("hero-open")
    }

    private func bannerFace(size: CGSize) -> some View {
        ZStack(alignment: .bottomLeading) {
            HeroBackground(
                poster: item.poster,
                backdrop: shownBackdrop,
                lite: lite,
                imageWidth: CatalogHeroMetrics.backdropWidth(forWidth: size.width, lite: lite)
            )
            .frame(width: size.width, height: size.height)
            KDBannerScrim()
            heroText(compact: size.width < 580, descriptionLines: size.height >= 330 ? 3 : 2)
                .frame(maxWidth: max(120, min(size.width - 48, 640)), alignment: .leading)
                .padding(24)
                .padding(.bottom, 54)
        }
        .frame(width: size.width, height: size.height)
        .clipShape(heroShape)
        .overlay(heroShape.strokeBorder(Color.white.opacity(0.12), lineWidth: 1).allowsHitTesting(false))
        .contentShape(heroShape)
    }

    private var accessibilityTitle: String {
        var parts: [String] = [item.title]
        for rating in item.ratings { parts.append(rating.source.spokenName + " " + rating.value) }
        parts.append(tr("Подробнее", "Details"))
        return parts.joined(separator: ", ")
    }

    private func heroText(compact: Bool, descriptionLines: Int) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            if let eyebrow {
                Label(eyebrow, systemImage: "sparkles")
                    .font(KDFont.kicker)
                    .foregroundStyle(KDPalette.accent)
            }
            Text(item.title)
                .font(compact ? KDFont.displayCompact : KDFont.display)
                .lineLimit(2)
                .settingsLayoutProbe("catalog.hero.title")
            metaRow
            if !item.description.isEmpty {
                Text(item.description)
                    .font(KDFont.secondary)
                    .foregroundStyle(Color.white.opacity(0.9))
                    .lineLimit(descriptionLines)
            }
        }
        .foregroundStyle(Color.white)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var metaText: String {
        var parts: [String] = []
        if item.year > 0 { parts.append(String(item.year)) }
        let genre = catalogGenreLine(item.genre, limit: 2)
        if !genre.isEmpty { parts.append(genre) }
        return parts.joined(separator: " · ")
    }

    private var metaRow: some View {
        HStack(spacing: 10) {
            if !item.ratings.isEmpty { KDRatingRow(ratings: item.ratings, valueColor: Color.white) }
            Text(metaText)
                .foregroundStyle(Color.white.opacity(0.85))
                .lineLimit(1)
            if let quality = item.quality, !quality.isEmpty { KDPosterBadge(text: tr(quality)) }
        }
        .font(KDFont.meta)
    }

    private var actionRow: some View {
        KDGlassGroup(spacing: 10) {
            HStack(spacing: 10) {
                Button { model.open(item) } label: { Label(tr("Подробнее", "Details"), systemImage: "info.circle") }
                    .buttonStyle(KDPrimaryButtonStyle())
                Button { Task { await model.save(card) } } label: {
                    Image(systemName: saved ? "heart.fill" : "heart")
                        .foregroundStyle(saved ? Color.pink : Color.white)
                }
                .buttonStyle(KDOverlayButtonStyle(circular: true))
                .help(saveTitle)
                .accessibilityLabel(saveTitle)
            }
        }
        .controlSize(.large)
        .padding(24)
        .settingsLayoutProbe("catalog.hero.actions")
    }
}

private struct HeroBackground: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    var poster: String?
    var backdrop: String?
    var lite: Bool
    var imageWidth: Int
    @State private var image: NSImage?

    init(poster: String?, backdrop: String?, lite: Bool, imageWidth: Int) {
        self.poster = poster
        self.backdrop = backdrop
        self.lite = lite
        self.imageWidth = imageWidth
        _image = State(initialValue: ImageStore.cachedImage(backdrop, width: imageWidth))
    }

    var body: some View {
        ZStack {
            KDBannerBase()
            if image == nil, !lite, !reduceTransparency {
                Artwork(address: poster, width: 180).blur(radius: 26, opaque: true).opacity(0.55)
            }
            if let image { Image(nsImage: image).resizable().scaledToFill().transition(.opacity) }
        }.clipped()
        .task(id: (backdrop ?? "") + (lite ? ":lite" : ":full") + ":" + String(imageWidth)) {
            guard let backdrop else { image = nil; return }
            if let ready = ImageStore.cachedImage(backdrop, width: imageWidth) { image = ready; return }
            image = nil
            let result = await ImageStore.shared.image(backdrop, width: imageWidth)
            if !Task.isCancelled { withAnimation(reduceMotion || lite ? nil : .easeInOut(duration: 0.25)) { image = result } }
        }
        .onDisappear { image = nil }
    }
}
