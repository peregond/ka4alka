import AppKit
import SwiftUI

// The "Основное" cards: Downloads, Appearance, Catalog, Sources, Ignored.

// MARK: Appearance

struct SettingsAppearanceCard: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.colorScheme) private var colorScheme
    /// The language as just picked; nil once the engine has answered, so the picker never snaps back meanwhile.
    @State private var languageLocal: String?

    private var cardIcon: String { colorScheme == .dark ? "moon" : "sun.max" }

    var body: some View {
        SettingsCard(.appearance, icon: cardIcon) {
            themeRow
            Divider()
            languageRow
            Divider()
            SettingsToggleRow(
                tr("Лёгкий режим", "Lite mode"),
                key: "liteMode",
                detail: tr("Меньше памяти для изображений и декоративных эффектов. Поиск и загрузки доступны полностью.", "Uses less artwork memory and fewer visual effects. Search and downloads remain fully available."),
                read: { model.settings?.liteMode ?? false }
            )
        }
    }

    private var themeRow: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            Text(tr("Тема", "Theme")).font(KDFont.rowTitle)
            HStack(spacing: KD.Space.s) {
                ForEach(AppearanceMode.allCases) { (mode: AppearanceMode) in
                    SettingsAppearanceTile(mode: mode, selected: model.appearance == mode) { model.setAppearance(mode) }
                }
            }
        }
    }

    private var languageBinding: Binding<String> {
        Binding<String>(
            get: { languageLocal ?? model.settings?.uiLanguage ?? "ru" },
            set: { (value: String) in
                languageLocal = value
                Task { @MainActor in
                    await model.set("uiLanguage", .string(value))
                    if languageLocal == value { languageLocal = nil }
                }
            }
        )
    }

    private var languageRow: some View {
        HStack {
            Text(tr("Язык интерфейса", "Interface language"))
            Spacer(minLength: KD.Space.m)
            Picker(tr("Язык интерфейса", "Interface language"), selection: languageBinding) {
                Text("Русский").tag("ru")
                Text("English").tag("en")
            }
            .labelsHidden()
            .frame(width: 145, height: 24)
            .contentShape(Rectangle())
            .settingsLayoutProbe("menu.settings.language", kind: .control)
        }
    }
}

private struct SettingsAppearanceTile: View {
    let mode: AppearanceMode
    let selected: Bool
    let action: () -> Void
    @Environment(\.colorSchemeContrast) private var contrast

    private var title: String {
        switch mode {
        case .system: return tr("Система", "System")
        case .light: return tr("Светлая", "Light")
        case .dark: return tr("Тёмная", "Dark")
        }
    }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: KD.Radius.control, style: .continuous)
        let weight: Font.Weight = selected ? .semibold : .regular
        let ringWidth: CGFloat = selected ? 2 : (contrast == .increased ? 1.5 : 1)
        Button(action: action) {
            VStack(spacing: KD.Space.xs) {
                SettingsAppearancePreview(mode: mode)
                    .frame(height: 54)
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
                    .accessibilityHidden(true)
                HStack(spacing: 4) {
                    if selected {
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundStyle(KDPalette.accent)
                            .accessibilityHidden(true)
                    }
                    Text(title).font(KDFont.secondary.weight(weight))
                }
            }
            .padding(KD.Space.xs)
            .frame(maxWidth: .infinity)
            .background(shape.fill(selected ? KDPalette.accentSoft : Color.primary.opacity(0.04)))
            .overlay(shape.strokeBorder(selected ? KDPalette.accent : KDPalette.hairline, lineWidth: ringWidth))
            .contentShape(shape)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(title)
        .accessibilityAddTraits(selected ? AccessibilityTraits.isSelected : AccessibilityTraits())
        .settingsLayoutProbe("appearance.tile." + mode.rawValue, kind: .control, group: "card.appearance")
    }
}

/// A miniature window in the colours of the theme it stands for. The preview colours are fixed on purpose.
private struct SettingsAppearancePreview: View {
    let mode: AppearanceMode

    var body: some View {
        switch mode {
        case .light:
            miniWindow(dark: false)
        case .dark:
            miniWindow(dark: true)
        case .system:
            ZStack {
                miniWindow(dark: false)
                miniWindow(dark: true).mask(rightHalf)
            }
        }
    }

    private var rightHalf: some View {
        HStack(spacing: 0) {
            Color.clear
            Color.black
        }
    }

    private func miniWindow(dark: Bool) -> some View {
        let background: Color = dark ? Color(red: 0.11, green: 0.12, blue: 0.14) : Color(red: 0.96, green: 0.96, blue: 0.97)
        let side: Color = dark ? Color(red: 0.17, green: 0.18, blue: 0.21) : Color(red: 0.89, green: 0.90, blue: 0.92)
        let bar: Color = dark ? Color.white.opacity(0.35) : Color.black.opacity(0.25)
        let mint: Color = Color(red: 0.25, green: 0.84, blue: 0.65)
        return ZStack(alignment: .topLeading) {
            background
            HStack(spacing: 0) {
                side.frame(width: 22)
                VStack(alignment: .leading, spacing: 5) {
                    RoundedRectangle(cornerRadius: 2, style: .continuous).fill(bar).frame(width: 46, height: 5)
                    RoundedRectangle(cornerRadius: 2, style: .continuous).fill(bar.opacity(0.6)).frame(width: 64, height: 5)
                    RoundedRectangle(cornerRadius: 2, style: .continuous).fill(mint).frame(width: 30, height: 5)
                }
                .padding(8)
                Spacer(minLength: 0)
            }
        }
    }
}

// MARK: Downloads

struct SettingsDownloadsCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var folderMissing = false
    @State private var notice: SettingsNotice?

    private var folder: String { model.settings?.folder ?? "" }
    private var configured: Bool { model.settings?.folderConfigured ?? false }

    var body: some View {
        SettingsCard(.downloads) {
            folderBlock
            Divider()
            SettingsToggleRow(
                tr("Продолжать активные загрузки при запуске", "Resume active downloads on launch"),
                key: "autoResumeDownloads",
                detail: tr("Загрузки на ручной паузе остаются на паузе.", "Downloads you paused by hand stay paused."),
                read: { model.settings?.autoResumeDownloads ?? true }
            )
            Divider()
            SettingsToggleRow(
                tr("Восстанавливать загрузки после обрыва сети", "Recover downloads after network interruptions"),
                key: "autoRecoverDownloads",
                detail: tr("Продолжим активные загрузки, когда вернётся сеть или компьютер проснётся.", "Active downloads continue when the network returns or the Mac wakes up."),
                read: { model.settings?.autoRecoverDownloads ?? true }
            )
            Divider()
            SpeedLimitsView()
        }
        .task(id: folder) { refreshFolder() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in refreshFolder() }
    }

    private var folderBlock: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            Text(tr("Папка загрузок", "Download folder")).font(KDFont.rowTitle)
            Text(configured ? folder : tr("Папка не выбрана", "No folder chosen"))
                .font(KDFont.secondary)
                .foregroundStyle(Color.secondary)
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
            if configured && folderMissing {
                Label(tr("Папка недоступна. Выбери другую папку для загрузок.", "The folder is unavailable. Choose another download folder."), systemImage: "exclamationmark.triangle.fill")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.warning)
                    .fixedSize(horizontal: false, vertical: true)
            }
            SettingsCaption(tr("Внутри выбранной папки Качалка создаёт Ka4alka. Уже добавленные загрузки остаются на прежнем месте.", "Ka4alka creates a Ka4alka folder inside the one you choose. Downloads you already added stay where they are."))
            ViewThatFits(in: .horizontal) {
                HStack(spacing: KD.Space.s) { folderButtons }
                VStack(alignment: .leading, spacing: KD.Space.s) { folderButtons }
            }
            .padding(.top, 2)
            SettingsNoticeLine(notice: $notice)
        }
    }

    @ViewBuilder private var folderButtons: some View {
        Button {
            Task { await changeFolder() }
        } label: {
            Label(tr("Изменить папку", "Change folder"), systemImage: "folder.badge.gearshape")
        }
        .buttonStyle(KDSecondaryButtonStyle())
        Button {
            openFolder()
        } label: {
            Label(tr("Открыть папку", "Open folder"), systemImage: "folder")
        }
        .buttonStyle(KDSecondaryButtonStyle())
        .disabled(!configured || folderMissing)
    }

    private func refreshFolder() {
        guard configured, !folder.isEmpty else { folderMissing = false; return }
        var isDirectory: ObjCBool = false
        let exists: Bool = FileManager.default.fileExists(atPath: folder, isDirectory: &isDirectory)
        folderMissing = !(exists && isDirectory.boolValue)
    }

    @MainActor private func changeFolder() async {
        let before: String? = model.settings?.folder
        await model.chooseFolder()
        refreshFolder()
        if let after = model.settings?.folder, after != before {
            notice = SettingsNotice.ok(tr("Папка загрузок сохранена.", "Download folder saved."))
        }
    }

    private func openFolder() {
        refreshFolder()
        guard !folderMissing else { return }
        if !NSWorkspace.shared.open(URL(fileURLWithPath: folder)) {
            notice = SettingsNotice.failure(tr("Не удалось открыть папку.", "The folder could not be opened."))
        }
    }
}

// MARK: Catalog

struct SettingsCatalogCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var countryNotice: SettingsNotice?
    @State private var refreshNotice: SettingsNotice?
    /// The country as just picked; nil once the engine has answered, so the picker never snaps back meanwhile.
    @State private var countryLocal: String?

    private var countries: [Choice] {
        model.countries.sorted { (first: Choice, second: Choice) -> Bool in
            first.label.localizedStandardCompare(second.label) == .orderedAscending
        }
    }

    private var countryBinding: Binding<String> {
        Binding<String>(
            get: { countryLocal ?? model.settings?.homeCountry ?? "" },
            set: { (value: String) in
                countryLocal = value
                Task { @MainActor in
                    await model.set("homeCountry", .string(value))
                    if countryLocal == value { countryLocal = nil }
                    if model.settings?.homeCountry == value {
                        countryNotice = SettingsNotice.ok(tr("Страна подборок сохранена.", "Country saved."))
                    }
                }
            }
        )
    }

    private var tvdbText: AttributedString {
        var text = AttributedString(tr("Описания сериалов, которых нет в русском каталоге — ", "Descriptions of series that are missing from the Russian catalog — "))
        var link = AttributedString("Metadata provided by TheTVDB")
        link.link = URL(string: "https://thetvdb.com/")
        text.append(link)
        text.append(AttributedString("."))
        return text
    }

    var body: some View {
        SettingsCard(.catalog) {
            countryRow
            refreshRow
            Divider()
            attribution
        }
    }

    /// The engine sends `homeCountry` only from a version that supports it; without it the picker would write nothing useful.
    @ViewBuilder private var countryRow: some View {
        if let current = model.settings?.homeCountry, countries.contains(where: { (choice: Choice) -> Bool in choice.key == current }) {
            VStack(alignment: .leading, spacing: 6) {
                Text(tr("Отечественное кино", "Domestic cinema")).font(KDFont.rowTitle)
                Picker(tr("Страна отечественных подборок", "Country of the domestic collections"), selection: countryBinding) {
                    ForEach(countries) { (choice: Choice) in
                        Text(choice.label).tag(choice.key)
                    }
                }
                .labelsHidden()
                .frame(maxWidth: 320, minHeight: 24, alignment: .leading)
                .contentShape(Rectangle())
                .settingsLayoutProbe("menu.settings.country", kind: .control)
                SettingsCaption(tr("Учитываем страну производства, в том числе совместного.", "We use the country of production, co-productions included."))
                SettingsNoticeLine(notice: $countryNotice)
            }
            Divider()
        }
    }

    private var refreshRow: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            HStack(spacing: KD.Space.s) {
                Button {
                    Task { await refreshCatalog() }
                } label: {
                    Label(tr("Обновить каталог", "Update catalog"), systemImage: "arrow.clockwise")
                }
                .buttonStyle(KDSecondaryButtonStyle())
                .disabled(model.refreshing)
                if model.refreshing { SettingsSpinner() }
            }
            SettingsNoticeLine(notice: $refreshNotice)
        }
    }

    private var attribution: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(tr("Источники данных", "Data sources")).font(KDFont.rowTitle)
            Link(destination: URL(string: "https://www.themoviedb.org/")!) {
                Text("The Movie Database (TMDB)").foregroundStyle(KDPalette.accent)
            }
            SettingsCaption(tr("Широкие фоны — TMDB. This product uses the TMDB API but is not endorsed or certified by TMDB.", "Wide backdrops come from TMDB. This product uses the TMDB API but is not endorsed or certified by TMDB."))
            Text(tvdbText)
                .font(KDFont.secondary)
                .foregroundStyle(Color.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    @MainActor private func refreshCatalog() async {
        guard !model.refreshing else { return }
        refreshNotice = nil
        let failure: String? = await model.refresh()
        if Task.isCancelled { return }
        if let message = failure {
            refreshNotice = SettingsNotice.failure(tr("Не удалось проверить каталог: ", "The catalog could not be checked: ") + message)
        } else {
            refreshNotice = SettingsNotice.ok(tr("Каталог обновлён. Новые названия появятся в разделах.", "Catalog updated. New titles will appear in the sections."))
        }
    }
}

// MARK: Sources

struct SettingsSourcesCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var sourceName = ""
    @State private var sourceEndpoint = ""
    @State private var sourceKey = ""
    @State private var customSources: [JSONValue] = []
    @State private var adderOpen = false
    @State private var adding = false
    @State private var sourceError: String?
    @State private var removalName: String?

    private var trimmedName: String { sourceName.trimmingCharacters(in: .whitespacesAndNewlines) }
    private var trimmedEndpoint: String { sourceEndpoint.trimmingCharacters(in: .whitespacesAndNewlines) }
    private var trimmedKey: String { sourceKey.trimmingCharacters(in: .whitespacesAndNewlines) }

    private var endpointValid: Bool {
        guard let url = URL(string: trimmedEndpoint), let scheme = url.scheme?.lowercased() else { return false }
        guard scheme == "http" || scheme == "https" else { return false }
        return !(url.host ?? "").isEmpty
    }
    private var canAdd: Bool { !trimmedName.isEmpty && endpointValid && !adding }

    private var removalBinding: Binding<Bool> {
        Binding<Bool>(
            get: { removalName != nil },
            set: { (shown: Bool) in if !shown { removalName = nil } }
        )
    }

    var body: some View {
        SettingsCard(.sources) {
            SettingsCaption(tr("Встроены те же поисковые источники, что и в Windows. Можно добавить свой Torznab-индексатор.", "Uses the same search sources as Windows. You can add a Torznab indexer."))
            ForEach(Array(customSources.enumerated()), id: \.offset) { _, source in
                sourceRow(source)
            }
            adder
            if let sourceError {
                Label(sourceError, systemImage: "exclamationmark.triangle.fill")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.warning)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        }
        .task { await loadSources() }
        .confirmationDialog(
            tr("Удалить источник?", "Remove this source?"),
            isPresented: removalBinding,
            titleVisibility: .visible,
            presenting: removalName
        ) { (name: String) in
            Button(tr("Удалить", "Remove"), role: .destructive) { Task { await removeSource(name) } }
            Button(tr("Отмена", "Cancel"), role: .cancel) {}
        } message: { (name: String) in
            Text(name)
        }
    }

    private func sourceRow(_ source: JSONValue) -> some View {
        let name: String = source["name"].string
        return HStack(spacing: KD.Space.m) {
            VStack(alignment: .leading, spacing: 4) {
                Text(name).fixedSize(horizontal: false, vertical: true)
                SettingsCaption(source["endpoint"].string)
            }
            Spacer(minLength: 0)
            Button(tr("Удалить", "Remove")) { removalName = name }
                .buttonStyle(KDSecondaryButtonStyle())
                .controlSize(.small)
                .accessibilityLabel(tr("Удалить источник ", "Remove source ") + name)
        }
    }

    private var adder: some View {
        DisclosureGroup(isExpanded: $adderOpen) {
            VStack(alignment: .leading, spacing: KD.Space.s) {
                sourceField(tr("Название", "Name")) {
                    TextField(tr("Название источника", "Source name"), text: $sourceName)
                        .onSubmit { Task { await addSource() } }
                }
                sourceField(tr("Адрес", "Endpoint")) {
                    TextField("https://indexer.example/api", text: $sourceEndpoint)
                        .onSubmit { Task { await addSource() } }
                }
                if !trimmedEndpoint.isEmpty && !endpointValid {
                    Label(tr("Адрес должен начинаться с http:// или https://", "The address must start with http:// or https://"), systemImage: "exclamationmark.triangle.fill")
                        .font(KDFont.secondary)
                        .foregroundStyle(KDPalette.warning)
                        .fixedSize(horizontal: false, vertical: true)
                }
                sourceField(tr("Ключ API", "API key")) {
                    SecureField(tr("Ключ API", "API key"), text: $sourceKey)
                        .onSubmit { Task { await addSource() } }
                }
                SettingsCaption(tr("Ключ сохраняется на этом Mac в папке данных приложения.", "The key is stored on this Mac in the app data folder."))
                HStack(spacing: KD.Space.s) {
                    Button(tr("Добавить", "Add")) { Task { await addSource() } }
                        .buttonStyle(KDSecondaryButtonStyle())
                        .disabled(!canAdd)
                    if adding { SettingsSpinner() }
                }
            }
            .textFieldStyle(.roundedBorder)
            .padding(.top, KD.Space.s)
        } label: {
            Text(tr("Добавить источник", "Add source"))
        }
    }

    private func sourceField<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(KDFont.secondary).foregroundStyle(Color.secondary)
            content().labelsHidden()
        }
    }

    @MainActor private func loadSources() async {
        if let result = try? await model.bridge.call("sources") { customSources = result["items"].array }
    }

    @MainActor private func addSource() async {
        guard canAdd else { return }
        adding = true
        sourceError = nil
        defer { adding = false }
        do {
            let parameters: [String: JSONValue] = ["name": .string(trimmedName), "endpoint": .string(trimmedEndpoint), "apiKey": .string(trimmedKey)]
            _ = try await model.bridge.call("sourceAdd", parameters)
            sourceName = ""
            sourceEndpoint = ""
            sourceKey = ""
            await loadSources()
        } catch {
            sourceError = model.message(for: error) ?? error.localizedDescription
        }
    }

    @MainActor private func removeSource(_ name: String) async {
        sourceError = nil
        do {
            _ = try await model.bridge.call("sourceRemove", ["name": .string(name)])
            await loadSources()
        } catch {
            sourceError = model.message(for: error) ?? error.localizedDescription
        }
    }
}

// MARK: Ignored

struct SettingsIgnoredCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var titleFilter = ""
    @State private var genresOpen = false
    /// Genres as the person just set them; nil once the engine has answered every write.
    @State private var editedGenres: Set<String>?
    @State private var pendingGenreWrites = 0

    private static let listLimit = 30
    private static let scrollThreshold = 8

    private var hiddenTitles: [HiddenTitle] { model.settings?.hidden ?? [] }

    private var filteredTitles: [HiddenTitle] {
        let needle: String = titleFilter.trimmingCharacters(in: .whitespacesAndNewlines)
        if needle.isEmpty { return hiddenTitles }
        return hiddenTitles.filter { (item: HiddenTitle) -> Bool in item.title.localizedCaseInsensitiveContains(needle) }
    }

    private var shownTitles: [HiddenTitle] { Array(filteredTitles.prefix(SettingsIgnoredCard.listLimit)) }

    private var sortedGenres: [Choice] {
        model.genres.sorted { (first: Choice, second: Choice) -> Bool in
            first.label.localizedStandardCompare(second.label) == .orderedAscending
        }
    }

    private var effectiveGenres: Set<String> { editedGenres ?? Set(model.settings?.hiddenGenres ?? []) }

    var body: some View {
        SettingsCard(.ignored) {
            titlesBlock
            Divider()
            genresBlock
        }
    }

    // MARK: Titles

    @ViewBuilder private var titlesBlock: some View {
        if hiddenTitles.isEmpty {
            Label(tr("Скрытых названий пока нет", "No hidden titles"), systemImage: "checkmark.circle")
                .foregroundStyle(Color.secondary)
                .padding(.vertical, 4)
        } else {
            VStack(alignment: .leading, spacing: KD.Space.xs) {
                Text(tr("Скрытые названия: ", "Hidden titles: ") + String(hiddenTitles.count)).font(KDFont.rowTitle)
                if hiddenTitles.count > SettingsIgnoredCard.scrollThreshold {
                    TextField(tr("Найти среди скрытых", "Search hidden titles"), text: $titleFilter)
                        .textFieldStyle(.roundedBorder)
                }
                titleRows
                if filteredTitles.count > SettingsIgnoredCard.listLimit {
                    SettingsCaption(tr("Показаны первые 30. Уточни поиск.", "Showing the first 30. Narrow the search."))
                } else if filteredTitles.isEmpty {
                    SettingsCaption(tr("Ничего не найдено.", "Nothing found."))
                }
            }
        }
    }

    @ViewBuilder private var titleRows: some View {
        if shownTitles.count > SettingsIgnoredCard.scrollThreshold {
            ScrollView {
                rowStack
            }
            .frame(height: 260)
        } else {
            rowStack
        }
    }

    private var rowStack: some View {
        VStack(alignment: .leading, spacing: 4) {
            ForEach(shownTitles) { (hidden: HiddenTitle) in
                HStack(spacing: KD.Space.m) {
                    Text(hidden.title).fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                    Button(tr("Вернуть", "Restore")) { Task { await model.unhide(hidden.id) } }
                        .buttonStyle(KDSecondaryButtonStyle())
                        .controlSize(.small)
                        .accessibilityLabel(tr("Вернуть ", "Restore ") + hidden.title)
                }
                .padding(.vertical, 2)
            }
        }
    }

    // MARK: Genres

    private var genresBlock: some View {
        DisclosureGroup(isExpanded: $genresOpen) {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: KD.Space.s, alignment: .leading)], alignment: .leading, spacing: KD.Space.xs) {
                ForEach(sortedGenres) { (genre: Choice) in
                    Toggle(isOn: genreBinding(genre.key)) {
                        Text(settingsCapitalized(genre.label))
                    }
                    .toggleStyle(.checkbox)
                }
            }
            .padding(.top, KD.Space.s)
        } label: {
            let count: Int = effectiveGenres.count
            Text(count > 0 ? tr("Скрытые жанры (\(count))", "Hidden genres (\(count))") : tr("Скрытые жанры", "Hidden genres"))
        }
    }

    private func genreBinding(_ key: String) -> Binding<Bool> {
        Binding<Bool>(
            get: { effectiveGenres.contains(key) },
            set: { (hidden: Bool) in setGenre(key, hidden: hidden) }
        )
    }

    /// Updates the checkbox at once and sends the whole set, so a quick second click builds on the first.
    private func setGenre(_ key: String, hidden: Bool) {
        var values: Set<String> = effectiveGenres
        if hidden { values.insert(key) } else { values.remove(key) }
        editedGenres = values
        pendingGenreWrites += 1
        let payload: [JSONValue] = values.sorted().map { (item: String) -> JSONValue in JSONValue.string(item) }
        Task { @MainActor in
            await model.set("hiddenGenres", .array(payload))
            pendingGenreWrites -= 1
            if pendingGenreWrites == 0 { editedGenres = nil }
        }
    }
}
