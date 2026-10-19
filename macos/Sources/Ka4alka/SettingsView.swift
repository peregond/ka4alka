import SwiftUI

/// The Settings page. Wide enough (detail column of 900 pt or more) it shows the Windows-style section list
/// next to a left-aligned column of cards; narrower it is a single column. The cards live in the Settings*Card files.
struct SettingsView: View {
    @State private var activeSection: SettingsSectionID = SettingsSectionID.downloads
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

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
        return ScrollViewReader { (proxy: ScrollViewProxy) in
            HStack(alignment: .top, spacing: 0) {
                if showsList {
                    SettingsSectionList(active: activeSection) { (section: SettingsSectionID) in jump(to: section, with: proxy) }
                        .frame(width: SettingsView.listWidth)
                        .padding(.leading, gutter)
                }
                ScrollView {
                    column(gutter: gutter)
                }
                .settingsLayoutProbe("viewport")
            }
            .onPreferenceChange(SettingsCardOffsetKey.self) { (offsets: [String: CGFloat]) in
                if showsList { follow(offsets) }
            }
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

    private func jump(to section: SettingsSectionID, with proxy: ScrollViewProxy) {
        activeSection = section
        if reduceMotion {
            proxy.scrollTo(section.rawValue, anchor: .top)
        } else {
            withAnimation(.easeInOut(duration: 0.25)) { proxy.scrollTo(section.rawValue, anchor: .top) }
        }
    }

    /// Highlights the last card whose top has reached the reading line below the visible top.
    private func follow(_ offsets: [String: CGFloat]) {
        guard let top = offsets[SettingsCardOffsetKey.topKey] else { return }
        var current: SettingsSectionID = SettingsSectionID.allCases[0]
        for section in SettingsSectionID.allCases {
            if let y = offsets[section.rawValue], y - top <= SettingsView.readingLine { current = section }
        }
        if current != activeSection { activeSection = current }
    }
}
