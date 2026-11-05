import AppKit
import SwiftUI

/// Only the selected slide owns artwork tasks. The carousel's clock sleeps while
/// offscreen, inactive, hovered, keyboard-focused, or in an accessibility/Lite mode.
struct CatalogSpotlightView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.accessibilityVoiceOverEnabled) private var voiceOver
    @Environment(\.kdSafeTop) private var safeTop
    var items: [MediaCard]
    /// How wide the column of the banner's text and controls may be. The banner itself always runs edge to edge.
    var columnMax: CGFloat = KD.Width.page
    @State private var selectedID: String?
    @State private var width: CGFloat = 900
    @State private var visible = false
    @State private var hovered = false
    @State private var paused = false
    @FocusState private var focusedControl: String?
    private var selected: MediaCard? { items.first { $0.id == selectedID } ?? items.first }
    private var reduced: Bool { reduceMotion || voiceOver || model.settings?.liteMode == true }
    private var automatic: Bool {
        HeroRotation.canAdvance(count: items.count, visible: visible, active: scenePhase == .active,
            interacting: hovered || focusedControl != nil, paused: paused,
            reduced: reduced)
    }
    private var clock: HeroRotation.Clock {
        HeroRotation.Clock(ids: items.map(\.id), selected: selected?.id, running: automatic)
    }

    var body: some View {
        if let selected {
            ZStack {
                FeaturedHero(card: selected, eyebrow: eyebrow(for: selected), hasPager: items.count > 1,
                             columnMax: columnMax, focusedControl: $focusedControl)
                    .id(selected.id)
                    .transition(.opacity)
            }
                // The banner runs edge to edge and up under the toolbar, so it is taller by the strip the toolbar covers.
                .containerRelativeFrame(.vertical) { length, _ in
                    CatalogHeroMetrics.height(forWidth: width, viewportHeight: length) + safeTop
                }
                .kdReadWidth($width)
                .animation(reduceMotion || model.settings?.liteMode == true ? nil : .easeInOut(duration: 0.4), value: selected.id)
                .preference(key: KDHeroPageKey.self, value: true)
                .overlay { if items.count > 1 { arrows.padding(.top, safeTop) } }
                .overlay(alignment: .bottom) { if items.count > 1 { pager(selected: selected) } }
                .kdBackdropExtension()
                .background(HeroVisibilityProbe { visible = $0 })
                .onHover { hovered = $0 }
                .task(id: clock) {
                    guard automatic else { return }
                    do { try await Task.sleep(for: .seconds(HeroRotation.interval)) } catch { return }
                    guard !Task.isCancelled, automatic else { return }
                    advance(1)
                }
                .accessibilityIdentifier("catalog-spotlight")
        }
    }

    private func eyebrow(for card: MediaCard) -> String {
        if card.section == "games" { return tr("Стоит поиграть", "Worth playing") }
        if items.first?.id == card.id { return tr("В центре внимания", "In the spotlight") }
        return tr("Стоит посмотреть", "Worth watching")
    }

    private var arrows: some View {
        HStack {
            arrow("chevron.left", title: tr("Предыдущий баннер", "Previous banner"), delta: -1)
            Spacer()
            arrow("chevron.right", title: tr("Следующий баннер", "Next banner"), delta: 1)
        }
        .padding(.horizontal, 12)
        .heroColumn(columnMax)
        .kdOnBanner()
    }

    private func arrow(_ symbol: String, title: String, delta: Int) -> some View {
        Button { advance(delta) } label: {
            Image(systemName: symbol).font(.system(size: 18, weight: .medium)).frame(width: 40, height: 44)
        }
        .buttonStyle(KDOverlayButtonStyle(circular: true, solid: model.settings?.liteMode == true))
        .focused($focusedControl, equals: symbol)
        .help(title).accessibilityLabel(title)
    }

    private func pager(selected: MediaCard) -> some View {
        HStack(spacing: 0) {
            ForEach(items) { item in
                Button { selectedID = item.id } label: {
                    Capsule()
                        .fill(Color.white.opacity(item.id == selected.id ? 1 : 0.45))
                        .frame(width: item.id == selected.id ? 24 : 7, height: 7)
                        .frame(width: 30, height: 28)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .focused($focusedControl, equals: item.id)
                .help(item.title).accessibilityLabel(item.title)
                .accessibilityAddTraits(item.id == selected.id ? AccessibilityTraits.isSelected : AccessibilityTraits())
            }
        }
        .padding(.bottom, 14)
        .frame(maxWidth: .infinity)
        .overlay(alignment: .trailing) { if !reduced { pauseButton } }
        .heroColumn(columnMax)
        .kdOnBanner()
    }

    private var pauseButton: some View {
            Button { paused.toggle() } label: {
                Image(systemName: paused ? "play.fill" : "pause.fill").frame(width: 28, height: 28)
            }
            .buttonStyle(KDOverlayButtonStyle(circular: true, solid: model.settings?.liteMode == true))
            .focused($focusedControl, equals: "pause")
            .help(paused ? tr("Продолжить смену баннеров", "Resume slideshow") : tr("Приостановить смену баннеров", "Pause slideshow"))
            .accessibilityLabel(paused ? tr("Продолжить смену баннеров", "Resume slideshow") : tr("Приостановить смену баннеров", "Pause slideshow"))
            .padding(.trailing, 18).padding(.bottom, 14)
    }

    private func advance(_ delta: Int) {
        selectedID = HeroRotation.next(in: items.map(\.id), selected: selected?.id, delta: delta)
    }
}

/// The dark banner base with shimmering lines, drawn while the first page is loading.
struct HeroPlaceholder: View {
    @Environment(\.kdSafeTop) private var safeTop
    @State private var width: CGFloat = 0
    var body: some View {
        GeometryReader { geometry in KDHeroSkeleton(height: geometry.size.height, rounded: false) }
            .containerRelativeFrame(.vertical) { length, _ in
                CatalogHeroMetrics.height(forWidth: width > 0 ? width : 900, viewportHeight: length) + safeTop
            }
            .kdReadWidth($width)
            .preference(key: KDHeroPageKey.self, value: true)
    }
}

extension View {
    /// Keeps the controls of a full-bleed banner in the same centred column as the page below it: the page's
    /// reading width and its side margins.
    func heroColumn(_ maximum: CGFloat = KD.Width.page) -> some View {
        frame(maxWidth: maximum)
            .padding(.horizontal, KD.gutter)
            .frame(maxWidth: .infinity)
    }
}

struct FeaturedHero: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard
    var eyebrow: String? = nil
    var hasPager = false
    var columnMax: CGFloat = KD.Width.page
    var focusedControl: FocusState<String?>.Binding
    @State private var backdrop: String?
    @State private var localized: MediaCard?
    private var language: String { model.settings?.uiLanguage ?? "ru" }
    private var item: MediaCard { catalogShownCard(card, local: localized, english: language == "en") }
    /// The loaded address, or the one remembered from an earlier showing, so a banner flip does not restart from the blurred poster.
    private var shownBackdrop: String? { backdrop ?? CatalogMemo.backdrops[card.id] }
    private var lite: Bool { model.settings?.liteMode == true }
    private var requestID: String { card.id + ":" + language }
    private var saved: Bool { model.isSaved(card) }
    private var saveTitle: String { saved ? tr("Удалить из сохранённого", "Remove from Saved") : tr("Сохранить", "Save") }
    @Environment(\.kdSafeTop) private var safeTop

    var body: some View {
        GeometryReader { geometry in
            // The banner is as wide as the window; its text keeps the page's column. The part under the toolbar is
            // artwork only, so the height that counts for the layout is what lies below it.
            let column = min(geometry.size.width, columnMax) - 2 * KD.gutter
            let visibleHeight = geometry.size.height - safeTop
            ZStack(alignment: .bottomLeading) {
                openButton(size: geometry.size)
                VStack(alignment: .leading, spacing: 20) {
                    heroText(compact: column < 680, short: visibleHeight < 340,
                             descriptionLines: visibleHeight < 340 ? 1 : (column < 680 ? 2 : 3))
                        .allowsHitTesting(false)
                    actionRow
                }
                .frame(maxWidth: max(120, min(column - 124, 620)), alignment: .leading)
                .padding(.leading, 62)
                .padding(.bottom, hasPager ? 58 : 28)
                .frame(maxWidth: .infinity, alignment: .leading)
                .heroColumn(columnMax)
            }
            .kdOnBanner()
        }
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
            .buttonStyle(.plain)
            .accessibilityLabel(accessibilityTitle)
            .accessibilityIdentifier("hero-open")
    }

    /// Square, edge to edge. The artwork and its scrim are extended under the sidebar and the toolbar (macOS 26 and
    /// later), so the glass sidebar and the toolbar buttons float over the picture instead of over a flat colour.
    private func bannerFace(size: CGSize) -> some View {
        ZStack(alignment: .bottomLeading) {
            ZStack(alignment: .bottomLeading) {
                HeroBackground(
                    poster: item.poster,
                    backdrop: shownBackdrop,
                    lite: lite,
                    imageWidth: CatalogHeroMetrics.backdropWidth(forWidth: size.width, lite: lite)
                )
                .frame(width: size.width, height: size.height)
                KDBannerScrim()
            }
            .frame(width: size.width, height: size.height)
        }
        .frame(width: size.width, height: size.height)
        .contentShape(Rectangle())
    }

    private var accessibilityTitle: String {
        var parts: [String] = [item.title]
        for rating in item.ratings { parts.append(rating.source.spokenName + " " + rating.value) }
        parts.append(tr("Подробнее", "Details"))
        return parts.joined(separator: ", ")
    }

    private func heroText(compact: Bool, short: Bool, descriptionLines: Int) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            if let eyebrow {
                Label(eyebrow, systemImage: "sparkles")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Color.white.opacity(0.9))
            }
            Text(item.title)
                .font(.system(size: short ? 26 : (compact ? 32 : 48), weight: .bold))
                .tracking(-0.7)
                .lineLimit(2)
                .settingsLayoutProbe("catalog.hero.title", kind: .text, group: "catalog.hero")
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
                    .buttonStyle(HeroPrimaryButtonStyle())
                    .focused(focusedControl, equals: "details")
                    .settingsLayoutProbe("catalog.hero.details", kind: .control, group: "catalog.hero.actions")
                Button { Task { await model.save(card) } } label: {
                    Image(systemName: saved ? "heart.fill" : "heart")
                        .font(.system(size: 18))
                        .foregroundStyle(saved ? KDPalette.like : Color.white)
                        .frame(width: 44, height: 44)
                }
                .buttonStyle(KDOverlayButtonStyle(circular: true, solid: model.settings?.liteMode == true))
                .focused(focusedControl, equals: "save")
                .settingsLayoutProbe("catalog.hero.save", kind: .control, group: "catalog.hero.actions")
                .help(saveTitle)
                .accessibilityLabel(saveTitle)
            }
        }
        .controlSize(.large)
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

/// Apple TV-like white pill; secondary actions use the native glass surface.
private struct HeroPrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.body.weight(.semibold))
            .foregroundStyle(Color.black)
            .padding(.horizontal, 24)
            .frame(height: 44)
            .background(Color.white.opacity(configuration.isPressed ? 0.8 : 1), in: Capsule())
            .contentShape(Capsule())
    }
}
