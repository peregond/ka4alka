import AppKit
import ServiceManagement
import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var model: AppModel
    @EnvironmentObject var updater: MacUpdater
    @State private var cacheBytes: Double = 0
    @State private var clearingCache = false
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var sourceName = ""
    @State private var sourceEndpoint = ""
    @State private var sourceKey = ""
    @State private var customSources: [JSONValue] = []
    var body: some View {
        Form {
            SwiftUI.Section(tr("Внешний вид", "Appearance")) {
                Picker(tr("Язык интерфейса", "Interface language"), selection: Binding(get: { model.settings?.uiLanguage ?? "ru" }, set: { value in Task { await model.set("uiLanguage", .string(value)) } })) { Text("Русский").tag("ru"); Text("English").tag("en") }
                setting(tr("Светлая тема", "Light appearance"), key: "light", value: model.settings?.light ?? false)
                setting(tr("Облегчённый режим", "Lite mode"), key: "liteMode", value: model.settings?.liteMode ?? false)
                Text(tr("Меньше памяти для изображений и декоративных эффектов. Поиск и загрузки доступны полностью.", "Uses less artwork memory and fewer visual effects. Search and downloads remain fully available.")).font(.caption).foregroundStyle(.secondary)
            }
            SwiftUI.Section(tr("Загрузки", "Downloads")) {
                LabeledContent(tr("Папка загрузок", "Download folder")) { Text(model.settings?.folder ?? "").font(.caption).textSelection(.enabled).lineLimit(2) }
                HStack { Button(tr("Изменить папку", "Change folder")) { Task { await model.chooseFolder() } }; Button(tr("Открыть папку", "Open folder")) { if let folder = model.settings?.folder { NSWorkspace.shared.open(URL(fileURLWithPath: folder)) } } }
                setting(tr("Продолжать активные загрузки при запуске", "Resume active downloads on launch"), key: "autoResumeDownloads", value: model.settings?.autoResumeDownloads ?? true)
                setting(tr("Восстанавливать загрузки после обрыва сети", "Recover downloads after network interruptions"), key: "autoRecoverDownloads", value: model.settings?.autoRecoverDownloads ?? true)
                setting(tr("Уведомления о загрузках", "Download notifications"), key: "notifyDownloads", value: model.settings?.notifyDownloads ?? true)
                Toggle(tr("Открывать при входе в систему", "Open at login"), isOn: Binding(get: { launchAtLogin }, set: { next in Task { do { if next { try SMAppService.mainApp.register() } else { try await SMAppService.mainApp.unregister() }; launchAtLogin = SMAppService.mainApp.status == .enabled } catch { model.error = error.localizedDescription } } }))
                SpeedLimitsView()
            }
            SwiftUI.Section(tr("Обновления", "Updates")) {
                LabeledContent(tr("Версия", "Version")) { Text(model.version) }
                setting(tr("Проверять обновления при запуске", "Check for updates on launch"), key: "checkForUpdates", value: model.settings?.checkForUpdates ?? true)
                setting(tr("Скачивать обновления автоматически", "Download updates automatically"), key: "autoUpdate", value: model.settings?.autoUpdate ?? true)
                Text(updater.statusText).font(.caption).foregroundStyle(.secondary)
                Button(updater.buttonTitle) { Task { await updater.primaryAction() } }.disabled(updater.busy)
                if updater.busy { ProgressView().controlSize(.small) }
                Link(tr("Что нового", "Release notes"), destination: URL(string: "https://github.com/peregond/ka4alka/releases/latest")!)
            }
            SwiftUI.Section(tr("Игнорируемое", "Ignored")) {
                Text(tr("Скрытые названия не появляются в ленте и подборках.", "Hidden titles are excluded from the feed and collections.")).font(.caption).foregroundStyle(.secondary)
                if model.settings?.hidden.isEmpty != false { Text(tr("Скрытых названий пока нет", "No hidden titles")).foregroundStyle(.secondary) }
                ForEach(model.settings?.hidden ?? []) { hidden in HStack { Text(hidden.title); Spacer(); Button(tr("Вернуть", "Restore")) { Task { await model.unhide(hidden.id) } }.buttonStyle(.borderless) } }
                DisclosureGroup(tr("Скрытые жанры", "Hidden genres")) {
                    ForEach(model.genres) { genre in Toggle(genre.label, isOn: Binding(get: { model.settings?.hiddenGenres.contains(genre.key) ?? false }, set: { hidden in var values = Set(model.settings?.hiddenGenres ?? []); if hidden { values.insert(genre.key) } else { values.remove(genre.key) }; Task { await model.set("hiddenGenres", .array(values.sorted().map(JSONValue.string))) } })) } }
                }
            }
            SwiftUI.Section(tr("Локальная сеть", "Local network")) {
                setting(tr("Разрешить связь с другими компьютерами", "Enable local network devices"), key: "lanEnabled", value: model.settings?.lanEnabled ?? false)
                Text(tr("Сопряжение работает с версиями для Windows и Mac. Подтверди одинаковый код на обоих компьютерах.", "Pair with Windows and Mac versions. Confirm the matching code on both computers.")).font(.caption).foregroundStyle(.secondary)
                if model.settings?.lanEnabled == true {
                    HStack { Button(tr("Найти устройства", "Find devices")) { Task { await model.lan("discover") } }; Button(tr("Разрешить сопряжение на 2 минуты", "Allow pairing for 2 minutes")) { Task { await model.lan("allow") } } }
                    if let name = model.lanState?.name { Text(name).font(.callout).foregroundStyle(.secondary) }
                    ForEach(model.lanState?.devices ?? []) { device in HStack { VStack(alignment: .leading) { Text(device.name); Text(device.address).font(.caption).foregroundStyle(.secondary) }; Spacer(); if device.paired { Label(tr("Подключено", "Paired"), systemImage: "checkmark.shield") } else { Button(tr("Соединить", "Pair")) { Task { await model.lan("pair", ["id": .string(device.id)]) } } } }
                }
            }
            SwiftUI.Section(tr("Источники", "Sources")) {
                Text(tr("Встроены те же поисковые источники, что и в Windows. Можно добавить свой Torznab-индексатор.", "Uses the same search sources as Windows. You can add a Torznab indexer.")).font(.caption).foregroundStyle(.secondary)
                ForEach(Array(customSources.enumerated()), id: \.offset) { _, source in HStack { VStack(alignment: .leading) { Text(source["name"].string); Text(source["endpoint"].string).font(.caption).foregroundStyle(.secondary) }; Spacer(); Button(tr("Удалить", "Remove")) { Task { _ = try? await model.bridge.call("sourceRemove", ["name": source["name"]]); await loadSources() } }.buttonStyle(.borderless) } }
                DisclosureGroup(tr("Добавить источник", "Add source")) {
                    TextField(tr("Название", "Name"), text: $sourceName)
                    TextField("https://indexer.example/api", text: $sourceEndpoint)
                    SecureField("API key", text: $sourceKey)
                    Button(tr("Добавить", "Add")) { Task { do { _ = try await model.bridge.call("sourceAdd", ["name": .string(sourceName), "endpoint": .string(sourceEndpoint), "apiKey": .string(sourceKey)]); sourceName = ""; sourceEndpoint = ""; sourceKey = ""; await loadSources() } catch { model.error = error.localizedDescription } } }.disabled(sourceName.isEmpty || sourceEndpoint.isEmpty)
                }
            }
            SwiftUI.Section(tr("Кэш", "Cache")) {
                Text(byteText(cacheBytes)).foregroundStyle(.secondary)
                Button(tr("Очистить кэш", "Clear cache")) {
                    clearingCache = true
                    Task {
                        do { let result = try await model.bridge.call("clearCache"); cacheBytes = result["bytes"].number; ImageStore.shared.clear() }
                        catch { model.error = error.localizedDescription }
                        clearingCache = false
                    }
                }.disabled(clearingCache)
                Text(tr("Настройки, сохранённое и загруженные файлы останутся на месте.", "Settings, saved titles and downloaded files are preserved.")).font(.caption).foregroundStyle(.secondary)
            }
            SwiftUI.Section(tr("Данные о фильмах", "Movie metadata")) {
                Link("The Movie Database (TMDB)", destination: URL(string: "https://www.themoviedb.org/")!)
                Text("This product uses the TMDB API but is not endorsed or certified by TMDB.").font(.caption).foregroundStyle(.secondary)
                Link("TheTVDB", destination: URL(string: "https://thetvdb.com/")!)
            }
            SwiftUI.Section(tr("Помощь и диагностика", "Help and diagnostics")) {
                HStack { Link(tr("Новости в Telegram", "News on Telegram"), destination: URL(string: "https://t.me/ka4alkacc")!); Link(tr("Сообщить об ошибке", "Report a bug"), destination: URL(string: "https://t.me/+6sT2ocaKvLxlNmRi")!) }
                Button(tr("Скопировать логи", "Copy logs")) { Task { await model.logs() } }
                Text(tr("Личные пути и ключи удаляются из диагностического отчёта.", "Private paths and credentials are removed from diagnostic reports.")).font(.caption).foregroundStyle(.secondary)
            }
        }.formStyle(.grouped).task { await loadSources(); if let usage = try? await model.bridge.call("cacheUsage") { cacheBytes = usage["bytes"].number }; if model.settings?.lanEnabled == true { await model.lan("discover") } }
    }
    private func setting(_ label: String, key: String, value: Bool) -> some View { Toggle(label, isOn: Binding(get: { value }, set: { next in Task { await model.set(key, .bool(next)) } })) }
    private func loadSources() async { let result = try? await model.bridge.call("sources"); customSources = result?["items"].array ?? [] }
}
