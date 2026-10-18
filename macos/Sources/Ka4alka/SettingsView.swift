import AppKit
import ServiceManagement
import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var updater: MacUpdater
    @State private var cacheBytes: Double = 0
    @State private var clearingCache = false
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var sourceName = ""
    @State private var sourceEndpoint = ""
    @State private var sourceKey = ""
    @State private var customSources: [JSONValue] = []

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                header
                appearance
                downloads
                updates
                ignored
                network
                sources
                cache
                metadata
                help
            }
            .frame(maxWidth: 860, alignment: .leading)
            .settingsLayoutProbe("content")
            .frame(maxWidth: .infinity, alignment: .center)
            .padding(28)
        }
        .settingsLayoutProbe("viewport")
        .background(Color(nsColor: .windowBackgroundColor))
        .task {
            await loadSources()
            if let usage = try? await model.bridge.call("cacheUsage") { cacheBytes = usage["bytes"].number }
            if model.settings?.lanEnabled == true { await model.lan("discover") }
        }
    }

    private var header: some View {
        HStack(spacing: 16) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 56, height: 56)
            VStack(alignment: .leading, spacing: 5) {
                Text(tr("Настройки", "Settings")).font(.largeTitle.bold())
                Text(tr("Сделай Качалку удобной для себя.", "Make Ka4alka work your way.")).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }.padding(.bottom, 4)
    }

    private var appearance: some View {
        SettingsCard(tr("Внешний вид", "Appearance"), icon: "paintpalette", id: "appearance") {
            HStack {
                Text(tr("Язык интерфейса", "Interface language"))
                Spacer(minLength: 16)
                Picker(tr("Язык интерфейса", "Interface language"), selection: Binding(
                    get: { model.settings?.uiLanguage ?? "ru" },
                    set: { value in Task { await model.set("uiLanguage", .string(value)) } }
                )) { Text("Русский").tag("ru"); Text("English").tag("en") }
                    .labelsHidden().frame(width: 145)
            }
            Divider()
            setting(tr("Светлая тема", "Light appearance"), key: "light", value: model.settings?.light ?? false)
            Divider()
            setting(tr("Облегчённый режим", "Lite mode"), key: "liteMode", value: model.settings?.liteMode ?? false,
                    detail: tr("Меньше памяти для изображений и декоративных эффектов. Поиск и загрузки доступны полностью.", "Uses less artwork memory and fewer visual effects. Search and downloads remain fully available."))
        }
    }

    private var downloads: some View {
        SettingsCard(tr("Загрузки", "Downloads"), icon: "arrow.down.circle", id: "downloads") {
            VStack(alignment: .leading, spacing: 10) {
                Text(tr("Папка загрузок", "Download folder")).font(.body.weight(.medium))
                Text(model.settings?.folder ?? "—").font(.callout).foregroundStyle(.secondary)
                    .textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 10) { folderButtons }
                    VStack(alignment: .leading, spacing: 10) { folderButtons }
                }.padding(.top, 2)
            }
            Divider()
            setting(tr("Продолжать активные загрузки при запуске", "Resume active downloads on launch"), key: "autoResumeDownloads", value: model.settings?.autoResumeDownloads ?? true)
            Divider()
            setting(tr("Восстанавливать загрузки после обрыва сети", "Recover downloads after network interruptions"), key: "autoRecoverDownloads", value: model.settings?.autoRecoverDownloads ?? true)
            Divider()
            setting(tr("Уведомления о загрузках", "Download notifications"), key: "notifyDownloads", value: model.settings?.notifyDownloads ?? true)
            Divider()
            SettingsSwitch(tr("Открывать при входе в систему", "Open at login"), isOn: Binding(
                get: { launchAtLogin },
                set: { next in Task {
                    do {
                        if next { try SMAppService.mainApp.register() } else { try await SMAppService.mainApp.unregister() }
                        launchAtLogin = SMAppService.mainApp.status == .enabled
                    } catch { model.error = error.localizedDescription }
                } }
            ))
            Divider()
            SpeedLimitsView().padding(.top, 4)
        }
    }

    @ViewBuilder private var folderButtons: some View {
        Button { Task { await model.chooseFolder() } } label: { Label(tr("Изменить папку", "Change folder"), systemImage: "folder.badge.gearshape") }
        Button {
            if let folder = model.settings?.folder { NSWorkspace.shared.open(URL(fileURLWithPath: folder)) }
        } label: { Label(tr("Открыть папку", "Open folder"), systemImage: "folder") }
    }

    private var updates: some View {
        SettingsCard(tr("Обновления", "Updates"), icon: "arrow.triangle.2.circlepath", id: "updates") {
            HStack { Text(tr("Версия приложения", "App version")); Spacer(); Text(model.version).monospacedDigit().foregroundStyle(.secondary) }
            Divider()
            setting(tr("Проверять обновления при запуске", "Check for updates on launch"), key: "checkForUpdates", value: model.settings?.checkForUpdates ?? true)
            Divider()
            setting(tr("Скачивать обновления автоматически", "Download updates automatically"), key: "autoUpdate", value: model.settings?.autoUpdate ?? true)
            Divider()
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: updater.ready ? "checkmark.circle.fill" : "info.circle").foregroundStyle(model.accent).padding(.top, 2)
                Text(updater.statusText).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
                if updater.busy { ProgressView().controlSize(.small) }
            }
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 16) { updateActions }
                VStack(alignment: .leading, spacing: 12) { updateActions }
            }
        }
    }

    @ViewBuilder private var updateActions: some View {
        Button(updater.buttonTitle) { Task { await updater.primaryAction() } }.buttonStyle(.borderedProminent).disabled(updater.busy)
        Link(tr("Что нового", "Release notes"), destination: URL(string: "https://github.com/peregond/ka4alka/releases/latest")!)
    }

    private var ignored: some View {
        SettingsCard(tr("Игнорируемое", "Ignored"), icon: "eye.slash", id: "ignored") {
            caption(tr("Скрытые названия не появляются в ленте и подборках.", "Hidden titles are excluded from the feed and collections."))
            if model.settings?.hidden.isEmpty != false {
                Label(tr("Скрытых названий пока нет", "No hidden titles"), systemImage: "checkmark.circle").foregroundStyle(.secondary).padding(.vertical, 4)
            }
            ForEach(model.settings?.hidden ?? []) { hidden in
                HStack(spacing: 16) {
                    Text(hidden.title).fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                    Button(tr("Вернуть", "Restore")) { Task { await model.unhide(hidden.id) } }
                }
            }
            Divider()
            DisclosureGroup(tr("Скрытые жанры", "Hidden genres")) {
                VStack(spacing: 12) {
                    ForEach(model.genres) { genre in
                        SettingsSwitch(genre.label, isOn: Binding(
                            get: { model.settings?.hiddenGenres.contains(genre.key) ?? false },
                            set: { hidden in
                                var values = Set(model.settings?.hiddenGenres ?? [])
                                if hidden { values.insert(genre.key) } else { values.remove(genre.key) }
                                Task { await model.set("hiddenGenres", .array(values.sorted().map(JSONValue.string))) }
                            }
                        ))
                    }
                }.padding(.top, 12)
            }
        }
    }

    private var network: some View {
        SettingsCard(tr("Локальная сеть", "Local network"), icon: "network", id: "lan") {
            setting(tr("Разрешить связь с другими компьютерами", "Enable local network devices"), key: "lanEnabled", value: model.settings?.lanEnabled ?? false,
                    detail: tr("Сопряжение работает с версиями для Windows и Mac. Подтверди одинаковый код на обоих компьютерах.", "Pair with Windows and Mac versions. Confirm the matching code on both computers."))
            if model.settings?.lanEnabled == true {
                Divider()
                ViewThatFits(in: .horizontal) {
                    HStack(spacing: 10) { pairingButtons }
                    VStack(alignment: .leading, spacing: 10) { pairingButtons }
                }
                if let name = model.lanState?.name { caption(name) }
                ForEach(model.lanState?.devices ?? []) { device in
                    HStack(spacing: 16) {
                        VStack(alignment: .leading, spacing: 4) { Text(device.name); caption(device.address) }
                        Spacer(minLength: 0)
                        if device.paired { Label(tr("Подключено", "Paired"), systemImage: "checkmark.shield").foregroundStyle(.secondary) }
                        else { Button(tr("Соединить", "Pair")) { Task { await model.lan("pair", ["id": .string(device.id)]) } } }
                    }
                }
            }
        }
    }

    @ViewBuilder private var pairingButtons: some View {
        Button(tr("Найти устройства", "Find devices")) { Task { await model.lan("discover") } }
        Button(tr("Разрешить сопряжение на 2 минуты", "Allow pairing for 2 minutes")) { Task { await model.lan("allow") } }
    }

    private var sources: some View {
        SettingsCard(tr("Источники", "Sources"), icon: "antenna.radiowaves.left.and.right", id: "sources") {
            caption(tr("Встроены те же поисковые источники, что и в Windows. Можно добавить свой Torznab-индексатор.", "Uses the same search sources as Windows. You can add a Torznab indexer."))
            ForEach(Array(customSources.enumerated()), id: \.offset) { _, source in
                HStack(spacing: 16) {
                    VStack(alignment: .leading, spacing: 4) { Text(source["name"].string); caption(source["endpoint"].string) }
                    Spacer(minLength: 0)
                    Button(tr("Удалить", "Remove")) { Task { _ = try? await model.bridge.call("sourceRemove", ["name": source["name"]]); await loadSources() } }
                }
            }
            DisclosureGroup(tr("Добавить источник", "Add source")) {
                VStack(alignment: .leading, spacing: 12) {
                    sourceField(tr("Название", "Name")) { TextField(tr("Название источника", "Source name"), text: $sourceName) }
                    sourceField(tr("Адрес", "Endpoint")) { TextField("https://indexer.example/api", text: $sourceEndpoint) }
                    sourceField("API key") { SecureField("API key", text: $sourceKey) }
                    Button(tr("Добавить", "Add")) { addSource() }.disabled(sourceName.isEmpty || sourceEndpoint.isEmpty)
                }.textFieldStyle(.roundedBorder).padding(.top, 12)
            }
        }
    }

    private var cache: some View {
        SettingsCard(tr("Кэш", "Cache"), icon: "internaldrive", id: "cache") {
            HStack {
                Text(tr("Занято на диске", "Disk usage")); Spacer()
                Text(byteText(cacheBytes)).monospacedDigit().foregroundStyle(.secondary)
            }
            caption(tr("Настройки, сохранённое и загруженные файлы останутся на месте.", "Settings, saved titles and downloaded files are preserved."))
            HStack(spacing: 12) {
                Button { clearCache() } label: { Label(tr("Очистить кэш", "Clear cache"), systemImage: "trash") }.disabled(clearingCache)
                if clearingCache { ProgressView().controlSize(.small) }
            }
        }
    }

    private var metadata: some View {
        SettingsCard(tr("Данные о фильмах", "Movie metadata"), icon: "film", id: "metadata") {
            Link("The Movie Database (TMDB)", destination: URL(string: "https://www.themoviedb.org/")!)
            caption("This product uses the TMDB API but is not endorsed or certified by TMDB.")
            Link("TheTVDB", destination: URL(string: "https://thetvdb.com/")!)
        }
    }

    private var help: some View {
        SettingsCard(tr("Помощь и диагностика", "Help and diagnostics"), icon: "questionmark.circle", id: "help") {
            ViewThatFits(in: .horizontal) {
                HStack(spacing: 20) { helpLinks }
                VStack(alignment: .leading, spacing: 12) { helpLinks }
            }
            Divider()
            Button { Task { await model.logs() } } label: { Label(tr("Скопировать логи", "Copy logs"), systemImage: "doc.on.doc") }
            caption(tr("Личные пути и ключи удаляются из диагностического отчёта.", "Private paths and credentials are removed from diagnostic reports."))
        }
    }

    @ViewBuilder private var helpLinks: some View {
        Link(destination: URL(string: "https://t.me/ka4alkacc")!) { Label(tr("Новости в Telegram", "News on Telegram"), systemImage: "paperplane") }
        Link(destination: URL(string: "https://t.me/+6sT2ocaKvLxlNmRi")!) { Label(tr("Сообщить об ошибке", "Report a bug"), systemImage: "bubble.left.and.bubble.right") }
    }

    private func setting(_ label: String, key: String, value: Bool, detail: String? = nil) -> some View {
        SettingsSwitch(label, detail: detail, isOn: Binding(get: { value }, set: { next in Task { await model.set(key, .bool(next)) } }))
    }
    private func caption(_ text: String) -> some View { Text(text).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true) }
    private func sourceField<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) { Text(title).font(.callout).foregroundStyle(.secondary); content().labelsHidden() }
    }
    private func loadSources() async { let result = try? await model.bridge.call("sources"); customSources = result?["items"].array ?? [] }
    private func addSource() {
        Task {
            do {
                _ = try await model.bridge.call("sourceAdd", ["name": .string(sourceName), "endpoint": .string(sourceEndpoint), "apiKey": .string(sourceKey)])
                sourceName = ""; sourceEndpoint = ""; sourceKey = ""; await loadSources()
            } catch { model.error = error.localizedDescription }
        }
    }
    private func clearCache() {
        clearingCache = true
        Task {
            do { let result = try await model.bridge.call("clearCache"); cacheBytes = result["bytes"].number; ImageStore.shared.clear() }
            catch { model.error = error.localizedDescription }
            clearingCache = false
        }
    }
}

// Explicit cards avoid macOS Form distributing nested sections across unrelated
// label/content columns. The scroll surface needs no blur or animated effects.
private struct SettingsCard<Content: View>: View {
    @EnvironmentObject private var model: AppModel
    let title: String
    let icon: String
    let id: String
    let content: Content

    init(_ title: String, icon: String, id: String, @ViewBuilder content: () -> Content) {
        self.title = title; self.icon = icon; self.id = id; self.content = content()
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(spacing: 11) {
                Image(systemName: icon).font(.system(size: 15, weight: .semibold)).foregroundStyle(model.accent)
                    .frame(width: 32, height: 32).background(model.accent.opacity(0.1), in: RoundedRectangle(cornerRadius: 9))
                Text(title).font(.title3.weight(.semibold))
            }
            VStack(alignment: .leading, spacing: 14) { content }.frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(22)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 18))
        .overlay(RoundedRectangle(cornerRadius: 18).strokeBorder(Color.primary.opacity(0.07), lineWidth: 1))
        .buttonStyle(.bordered)
        .controlSize(.large)
        .settingsLayoutProbe("card." + id)
    }
}

private struct SettingsSwitch: View {
    let title: String
    let detail: String?
    @Binding var isOn: Bool
    init(_ title: String, detail: String? = nil, isOn: Binding<Bool>) { self.title = title; self.detail = detail; self._isOn = isOn }
    var body: some View {
        HStack(alignment: .center, spacing: 20) {
            VStack(alignment: .leading, spacing: 6) {
                Text(title).fixedSize(horizontal: false, vertical: true)
                if let detail { Text(detail).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true) }
            }
            Spacer(minLength: 0)
            Toggle(title, isOn: $isOn).labelsHidden().toggleStyle(.switch).controlSize(.regular).fixedSize()
        }.accessibilityElement(children: .contain)
    }
}
