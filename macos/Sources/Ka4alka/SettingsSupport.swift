import AppKit
import SwiftUI

// Shared pieces of the Settings page: the section list model, the card chrome,
// the switch row and the inline feedback line. Everything here is prefixed
// "Settings" so it cannot collide with another group's names.

// MARK: Sections

enum SettingsGroupID: String, CaseIterable, Identifiable {
    case general, system, upkeep
    var id: String { rawValue }
    var title: String {
        switch self {
        case .general: return tr("Основное", "General")
        case .system: return tr("Система", "System")
        case .upkeep: return tr("Обслуживание", "Maintenance")
        }
    }
    var sections: [SettingsSectionID] {
        SettingsSectionID.allCases.filter { (section: SettingsSectionID) -> Bool in section.group == self }
    }
}

/// Every card of the page. The declaration order is the order on screen and in the section list.
enum SettingsSectionID: String, CaseIterable, Identifiable {
    case downloads, appearance, catalog, sources, ignored
    case notifications, lan, startup
    case updates, community, cache, help

    var id: String { rawValue }

    var group: SettingsGroupID {
        switch self {
        case .downloads, .appearance, .catalog, .sources, .ignored: return .general
        case .notifications, .lan, .startup: return .system
        case .updates, .community, .cache, .help: return .upkeep
        }
    }

    var title: String {
        switch self {
        case .downloads: return tr("Загрузки", "Downloads")
        case .appearance: return tr("Внешний вид", "Appearance")
        case .catalog: return tr("Каталог", "Catalog")
        case .sources: return tr("Источники", "Sources")
        case .ignored: return tr("Игнорируемое", "Ignored")
        case .notifications: return tr("Уведомления", "Notifications")
        case .lan: return tr("Устройства в сети", "Devices on your network")
        case .startup: return tr("Запуск", "Launch")
        case .updates: return tr("Обновления", "Updates")
        case .community: return tr("Новости и поддержка", "News and support")
        case .cache: return tr("Кэш", "Cache")
        case .help: return tr("Помощь и диагностика", "Help and diagnostics")
        }
    }

    var subtitle: String {
        switch self {
        case .downloads: return tr("Папка, возобновление и свободное место.", "Folder, resuming and free space.")
        case .appearance: return tr("Язык, тема и режим для слабых компьютеров.", "Language, theme and a mode for slower computers.")
        case .catalog: return tr("Страна для подборок «Отечественное» и источники данных.", "Home country for the domestic collections and data sources.")
        case .sources: return tr("Свои индексаторы для поиска раздач.", "Your own indexers for finding releases.")
        case .ignored: return tr("Скрой названия и жанры в каталоге и подборках. В поиске, «Сохранённом» и загрузках они остаются.", "Hide titles and genres from the catalog and collections. They stay in search, Saved and downloads.")
        case .notifications: return tr("Сообщим, когда загрузка завершится, остановится с ошибкой или упрётся в нехватку места.", "We tell you when a download finishes, fails or runs out of space.")
        case .lan: return tr("Отправляй загрузки на другой компьютер в домашней сети.", "Send downloads to another computer on your home network.")
        case .startup: return tr("Открывать Качалку вместе с macOS.", "Open Ka4alka when you log in to macOS.")
        case .updates: return tr("Автоматическая проверка и установка новых версий.", "Automatic checking and installing of new versions.")
        case .community: return tr("Новости Качалки и чат поддержки в Telegram.", "Ka4alka news and the support chat on Telegram.")
        case .cache: return tr("Постеры, фото и данные каталога занимают до 1 ГБ. Лишнее удаляется само.", "Posters, photos and catalog data take up to 1 GB. The excess is removed automatically.")
        case .help: return tr("Отчёт для поддержки. Личные пути и ключи доступа скрываются.", "A report for support. Private paths and access keys are removed.")
        }
    }

    var icon: String {
        switch self {
        case .downloads: return "arrow.down.circle"
        case .appearance: return "paintpalette"
        case .catalog: return "film"
        case .sources: return "antenna.radiowaves.left.and.right"
        case .ignored: return "eye.slash"
        case .notifications: return "bell"
        case .lan: return "network"
        case .startup: return "power"
        case .updates: return "arrow.triangle.2.circlepath"
        case .community: return "paperplane"
        case .cache: return "internaldrive"
        case .help: return "questionmark.circle"
        }
    }
}

// MARK: Scroll spy

/// Global minY of every card (keyed by section raw value) plus the top of the visible area under `topKey`.
struct SettingsCardOffsetKey: PreferenceKey {
    static let topKey = "settings.visible-top"
    static let defaultValue: [String: CGFloat] = [:]
    static func reduce(value: inout [String: CGFloat], nextValue: () -> [String: CGFloat]) {
        value.merge(nextValue()) { (_: CGFloat, new: CGFloat) -> CGFloat in new }
    }
}

// MARK: Section list

/// The Windows-style jump list: grouped entries, the active one tinted. Shown only when the page is wide enough.
struct SettingsSectionList: View {
    var active: SettingsSectionID
    var onSelect: (SettingsSectionID) -> Void

    var body: some View {
        VStack(spacing: 0) {
            Color.clear
                .frame(height: 0)
                .background(topMarker)
            ScrollView {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(SettingsGroupID.allCases) { (group: SettingsGroupID) in
                        groupLabel(group)
                        ForEach(group.sections) { (section: SettingsSectionID) in
                            row(section)
                        }
                    }
                }
                .padding(.top, KD.gutter)
                .padding(.bottom, KD.Space.m)
            }
            .scrollIndicators(.hidden)
        }
        .settingsLayoutProbe("settings.nav")
    }

    private var topMarker: some View {
        GeometryReader { (proxy: GeometryProxy) in
            Color.clear.preference(key: SettingsCardOffsetKey.self, value: [SettingsCardOffsetKey.topKey: proxy.frame(in: .global).minY])
        }
    }

    private func groupLabel(_ group: SettingsGroupID) -> some View {
        Text(group.title)
            .font(KDFont.kicker)
            .textCase(.uppercase)
            .foregroundStyle(Color.secondary)
            .padding(.horizontal, KD.Space.s)
            .padding(.top, group == .general ? KD.Space.xxs : KD.Space.m)
            .padding(.bottom, KD.Space.xxs)
            .accessibilityAddTraits(.isHeader)
    }

    @MainActor private func row(_ section: SettingsSectionID) -> some View {
        let selected: Bool = section == active
        let shape = RoundedRectangle(cornerRadius: KD.Radius.chip, style: .continuous)
        return Button {
            onSelect(section)
        } label: {
            HStack(spacing: KD.Space.xs) {
                Image(systemName: section.icon)
                    .font(.system(size: 14, weight: .regular))
                    .frame(width: 20)
                    .accessibilityHidden(true)
                Text(section.title)
                    .font(selected ? Font.body.weight(.semibold) : Font.body)
                    .lineLimit(1)
                Spacer(minLength: 0)
            }
            .foregroundStyle(selected ? KDPalette.accent : Color.primary)
            .padding(.horizontal, KD.Space.s)
            .frame(height: 32)
            .background(shape.fill(selected ? KDPalette.accentSoft : Color.clear))
            .contentShape(shape)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(tr("Раздел: ", "Section: ") + section.title)
        .accessibilityAddTraits(selected ? AccessibilityTraits.isSelected : AccessibilityTraits())
        .settingsLayoutProbe("settings.nav." + section.rawValue, kind: .control, group: "settings.nav")
    }
}

// MARK: Card

/// One settings card: icon plate, title with the Windows subtitle, rows below. Reports its position for the scroll spy.
struct SettingsCard<Content: View>: View {
    let section: SettingsSectionID
    let icon: String
    let content: Content

    init(_ section: SettingsSectionID, icon: String? = nil, @ViewBuilder content: () -> Content) {
        self.section = section
        self.icon = icon ?? section.icon
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            header
            VStack(alignment: .leading, spacing: KD.Space.s) { content }
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(KD.Space.m)
        .frame(maxWidth: .infinity, alignment: .leading)
        .kdCard(radius: KD.Radius.card)
        .controlSize(.regular)
        .background(offsetReader)
        .settingsLayoutProbe("card." + section.rawValue)
    }

    private var header: some View {
        HStack(alignment: .top, spacing: KD.Space.s) {
            Image(systemName: icon)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(KDPalette.accent)
                .frame(width: 32, height: 32)
                .background(KDPalette.accentSoft, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(section.title)
                    .font(KDFont.cardTitle)
                    .accessibilityAddTraits(.isHeader)
                Text(section.subtitle)
                    .font(KDFont.secondary)
                    .foregroundStyle(Color.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
    }

    private var offsetReader: some View {
        GeometryReader { (proxy: GeometryProxy) in
            Color.clear.preference(key: SettingsCardOffsetKey.self, value: [section.rawValue: proxy.frame(in: .global).minY])
        }
    }
}

// MARK: Rows

/// A row with a title, an optional explanation and a switch. The whole row toggles; VoiceOver reads it once, as the switch.
struct SettingsSwitch: View {
    let title: String
    let detail: String?
    @Binding var isOn: Bool

    init(_ title: String, detail: String? = nil, isOn: Binding<Bool>) {
        self.title = title
        self.detail = detail
        self._isOn = isOn
    }

    var body: some View {
        HStack(alignment: .center, spacing: KD.Space.m) {
            VStack(alignment: .leading, spacing: 4) {
                Text(title).fixedSize(horizontal: false, vertical: true)
                if let detail {
                    Text(detail)
                        .font(KDFont.secondary)
                        .foregroundStyle(Color.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .accessibilityHidden(true)
            Spacer(minLength: 0)
            Toggle(title, isOn: $isOn)
                .labelsHidden()
                .toggleStyle(.switch)
                .fixedSize()
                .accessibilityHint(detail ?? "")
        }
        .contentShape(Rectangle())
        .onTapGesture { isOn.toggle() }
    }
}

/// A switch bound to one boolean engine setting. It flips at once and writes afterwards, so a fast second click is never lost
/// and the switch never snaps back while the engine answers. When the write fails the switch returns to the engine's value.
struct SettingsToggleRow: View {
    @EnvironmentObject private var model: AppModel
    let title: String
    let detail: String?
    let key: String
    let read: () -> Bool
    let afterWrite: ((Bool) async -> Void)?
    @State private var local: Bool
    @State private var lastSent: Bool
    @State private var pending = 0

    init(_ title: String, key: String, detail: String? = nil, read: @escaping () -> Bool, afterWrite: ((Bool) async -> Void)? = nil) {
        self.title = title
        self.key = key
        self.detail = detail
        self.read = read
        self.afterWrite = afterWrite
        let initial: Bool = read()
        self._local = State(initialValue: initial)
        self._lastSent = State(initialValue: initial)
    }

    var body: some View {
        let remote: Bool = read()
        SettingsSwitch(title, detail: detail, isOn: $local)
            .onChange(of: local) { _, next in send(next) }
            .onChange(of: remote) { _, value in
                if pending == 0 { lastSent = value; local = value }
            }
    }

    private func send(_ next: Bool) {
        guard next != lastSent else { return }
        lastSent = next
        pending += 1
        Task { @MainActor in
            await model.set(key, .bool(next))
            pending -= 1
            if pending == 0 {
                let actual: Bool = read()
                if actual != local { lastSent = actual; local = actual }
                else if let afterWrite { await afterWrite(actual) }
            }
        }
    }
}

// MARK: Inline feedback

/// A short result line under an action ("Папка сохранена", "Не удалось…").
struct SettingsNotice: Equatable {
    let id = UUID()
    var text: String
    var isError: Bool
    static func ok(_ text: String) -> SettingsNotice { SettingsNotice(text: text, isError: false) }
    static func failure(_ text: String) -> SettingsNotice { SettingsNotice(text: text, isError: true) }
}

/// Shows the notice, announces it to VoiceOver and clears a success message after a few seconds. Errors stay.
struct SettingsNoticeLine: View {
    @Binding var notice: SettingsNotice?

    var body: some View {
        Group {
            if let value = notice {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Image(systemName: value.isError ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                        .foregroundStyle(value.isError ? KDPalette.warning : KDPalette.success)
                        .accessibilityHidden(true)
                    Text(value.text)
                        .font(KDFont.secondary)
                        .foregroundStyle(value.isError ? KDPalette.warning : Color.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                }
            }
        }
        .task(id: notice?.id) {
            guard let value = notice else { return }
            AccessibilityNotification.Announcement(value.text).post()
            if value.isError { return }
            try? await Task.sleep(for: .seconds(4))
            if !Task.isCancelled, notice == value { notice = nil }
        }
    }
}

/// Small grey explanation text.
struct SettingsCaption: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text)
            .font(KDFont.secondary)
            .foregroundStyle(Color.secondary)
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// A labelled spinner for a pending action.
struct SettingsSpinner: View {
    var body: some View {
        ProgressView()
            .controlSize(.small)
            .accessibilityLabel(tr("Загрузка", "Loading"))
    }
}

/// The first letter in upper case, the rest untouched ("боевик" becomes "Боевик").
func settingsCapitalized(_ text: String) -> String {
    guard let first = text.first else { return text }
    return String(first).uppercased() + String(text.dropFirst())
}
