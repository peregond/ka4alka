import SwiftUI

/// The Settings page. Wide enough (detail column of 900 pt or more) it shows the Windows-style section list
/// next to a left-aligned column of cards; narrower it is a single column. The cards live in the Settings*Card files.
struct SettingsView: View {
    @State private var activeSection: SettingsSectionID = SettingsSectionID.downloads
    @State private var scrollTarget: String?
    @State private var pinnedSection: SettingsSectionID?
    @State private var pinnedUntil: Date?
    @State private var pinnedSettledOffset: CGFloat?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// How long after a click the highlight ignores scroll position (covers the 0.25 s animated jump).
    private static let pinDuration: TimeInterval = 0.6

    private static let listBreakpoint: CGFloat = 900
    private static let listWidth: CGFloat = 220
    /// A card counts as "current" once its top is within this distance of the visible top (same rule as Windows).
    private static let readingLine: CGFloat = 90

    var body: some View {
        GeometryReader { (geometry: GeometryProxy) in
            layout(width: geometry.size.width)
        }
        .background(KDPalette.pageBackground)
    }

    private func layout(width: CGFloat) -> some View {
        let showsList: Bool = width >= SettingsView.listBreakpoint
        let gutter: CGFloat = KD.gutter(forWidth: width)
        return HStack(alignment: .top, spacing: 0) {
            if showsList {
                SettingsSectionList(active: activeSection) { (section: SettingsSectionID) in jump(to: section) }
                    .frame(width: SettingsView.listWidth)
                    .padding(.leading, gutter)
            }
            ScrollView {
                column(gutter: gutter)
            }
            .scrollPosition(id: $scrollTarget, anchor: .top)
            .settingsLayoutProbe("viewport")
        }
        .onPreferenceChange(SettingsCardOffsetKey.self) { (offsets: [String: CGFloat]) in
            if showsList { follow(offsets) }
        }
    }

    private func column(gutter: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            KDPageHeader(tr("Настройки", "Settings"), subtitle: tr("Всё сохраняется сразу.", "Changes are saved automatically."))
                .padding(.bottom, KD.Space.xs)
                .settingsLayoutProbe("header")
            ForEach(SettingsSectionID.allCases) { (section: SettingsSectionID) in
                card(for: section).id(section.rawValue)
            }
        }
        .scrollTargetLayout()
        .frame(maxWidth: KD.Width.settings, alignment: .leading)
        .settingsLayoutProbe("content")
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(gutter)
    }

    @ViewBuilder private func card(for section: SettingsSectionID) -> some View {
        switch section {
        case .downloads: SettingsDownloadsCard()
        case .appearance: SettingsAppearanceCard()
        case .catalog: SettingsCatalogCard()
        case .sources: SettingsSourcesCard()
        case .ignored: SettingsIgnoredCard()
        case .notifications: SettingsNotificationsCard()
        case .lan: SettingsLanCard()
        case .startup: SettingsStartupCard()
        case .updates: SettingsUpdatesCard()
        case .community: SettingsCommunityCard()
        case .cache: SettingsCacheCard()
        case .help: SettingsHelpCard()
        }
    }

    private func jump(to section: SettingsSectionID) {
        activeSection = section
        pinnedSection = section
        pinnedUntil = Date().addingTimeInterval(SettingsView.pinDuration)
        pinnedSettledOffset = nil
        let target: String = section.rawValue
        let animation: Animation? = reduceMotion ? nil : Animation.easeInOut(duration: 0.25)
        if scrollTarget == target {
            // The page already reports this card as its top item; reset the position so the jump happens again.
            scrollTarget = nil
            DispatchQueue.main.async {
                withAnimation(animation) { scrollTarget = target }
            }
        } else {
            withAnimation(animation) { scrollTarget = target }
        }
    }

    /// Highlights the last card whose top has reached the reading line below the visible top.
    private func follow(_ offsets: [String: CGFloat]) {
        guard let top = offsets[SettingsCardOffsetKey.topKey] else { return }
        if holdsPinnedSection(offsets, top: top) { return }
        var current: SettingsSectionID = SettingsSectionID.allCases[0]
        for section in SettingsSectionID.allCases {
            if let y = offsets[section.rawValue], y - top <= SettingsView.readingLine { current = section }
        }
        if current != activeSection { activeSection = current }
    }

    /// True while the section the user just clicked must stay highlighted: during the animated jump, and afterwards
    /// as long as the clicked card stays where the jump left it (the last cards cannot reach the reading line).
    /// Any scroll by the user moves the card and releases the pin.
    private func holdsPinnedSection(_ offsets: [String: CGFloat], top: CGFloat) -> Bool {
        guard let pinned = pinnedSection else { return false }
        guard let cardY = offsets[pinned.rawValue] else { return false }
        let relative: CGFloat = cardY - top
        if let until = pinnedUntil, Date() < until {
            pinnedSettledOffset = relative
            return true
        }
        if let settled = pinnedSettledOffset, abs(relative - settled) <= 2 { return true }
        pinnedSection = nil
        pinnedUntil = nil
        pinnedSettledOffset = nil
        return false
    }
}
