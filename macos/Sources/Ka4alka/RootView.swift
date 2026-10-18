import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct RootView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var updater: MacUpdater
    @FocusState private var searchFocused: Bool
    var body: some View {
        NavigationSplitView {
            VStack(spacing: 0) {
                HStack(spacing: 10) { Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 34, height: 34); Text(tr("Качалка", "Ka4alka")).font(.title2.bold()); Spacer() }.padding(18)
                List {
                    ForEach(Section.allCases.filter { $0 != .downloads && $0 != .settings }) { entry in sidebar(entry) }
                    SwiftUI.Section { sidebar(.downloads); sidebar(.settings) }
                }.listStyle(.sidebar)
                HStack { Text(model.version).font(.caption).foregroundStyle(.secondary); Spacer(); if updater.available { Button { Task { await updater.primaryAction() } } label: { Label(updater.buttonTitle, systemImage: "arrow.triangle.2.circlepath") }.buttonStyle(.borderless) } }.padding(14)
            }.navigationSplitViewColumnWidth(min: 170, ideal: 200, max: 240)
        } detail: {
            VStack(spacing: 0) {
                if let error = model.error {
                    HStack { Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange); Text(error).font(.callout).lineLimit(3); Spacer(); Button(tr("Повторить", "Retry")) { Task { await retry() } }; Button { model.error = nil } label: { Image(systemName: "xmark") }.buttonStyle(.plain) }.padding(12).background(Color.orange.opacity(0.08))
                }
                if !model.connected {
                    ContentUnavailableView { Label(tr("Открываем медиатеку", "Opening your library"), systemImage: "square.grid.2x2") } description: { Text(tr("Интерфейс готов. Подключаем каталог и очередь загрузок.", "The interface is ready. Connecting the catalog and download queue.")) } actions: { if model.error != nil { Button(tr("Повторить", "Retry")) { Task { await model.bootstrap() } } } else { ProgressView().controlSize(.small) } }
                } else {
                    content.id(model.route.key)
                }
                if let toast = model.toast { HStack { Text(toast).font(.caption); Spacer(); Button { model.toast = nil } label: { Image(systemName: "xmark") }.buttonStyle(.plain) }.padding(10).background(.quaternary) }
            }.background(Color(nsColor: .windowBackgroundColor))
                .toolbar {
                    ToolbarItem(placement: .navigation) { Button { model.back() } label: { Image(systemName: "chevron.left") }.disabled(model.routes.count < 2).help(tr("Назад", "Back")) }
                    ToolbarItem(placement: .principal) {
                        HStack(spacing: 7) { Image(systemName: "magnifyingglass").foregroundStyle(.secondary); TextField(tr("Найти фильм, сериал или человека", "Find a movie, series or person"), text: $model.query).textFieldStyle(.plain).focused($searchFocused).onSubmit { model.submit() }; if !model.query.isEmpty { Button { model.query = ""; if !model.submittedQuery.isEmpty { model.submit() } } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }.buttonStyle(.plain) } }.padding(8).background(Color.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 9)).frame(minWidth: 240, idealWidth: 440, maxWidth: 700)
                    }
                    ToolbarItemGroup(placement: .primaryAction) {
                        Button { model.addTorrent = true } label: { Image(systemName: "plus") }.help(tr("Добавить торрент", "Add torrent"))
                        Button { Task { await model.set("light", .bool(!(model.settings?.light ?? false))) } } label: { Image(systemName: model.settings?.light == true ? "sun.max.fill" : "moon.fill") }.help(tr("Сменить тему", "Change appearance"))
                    }
                }
                .onReceive(NotificationCenter.default.publisher(for: Notification.Name("Ka4alkaFocusSearch"))) { _ in searchFocused = true }
        }
        .navigationSplitViewStyle(.balanced)
        .tint(model.accent)
        .sheet(isPresented: $model.addTorrent) { AddTorrentSheet() }
        .sheet(isPresented: $model.folderRequired) {
            VStack(alignment: .leading, spacing: 18) {
                Image(systemName: "folder.badge.plus").font(.system(size: 40)).foregroundStyle(model.accent)
                Text(tr("Куда скачивать файлы?", "Where should downloads be saved?")).font(.title2.bold())
                Text(tr("Выбери папку. Внутри мы создадим Ka4alka и будем сохранять туда новые загрузки.", "Choose a folder. We will create a Ka4alka folder inside it for new downloads.")).foregroundStyle(.secondary)
                Button(tr("Выбрать папку", "Choose folder")) { Task { await model.chooseFolder() } }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
            }.padding(28).frame(width: 430).interactiveDismissDisabled()
        }
        .alert(tr("Сопряжение устройств", "Pair devices"), isPresented: Binding(get: { model.pairingPrompt != nil }, set: { if !$0 { Task { await model.approvePairing(false) } } })) {
            Button(tr("Коды совпадают", "Codes match")) { Task { await model.approvePairing(true) } }
            Button(tr("Отмена", "Cancel"), role: .cancel) { Task { await model.approvePairing(false) } }
        } message: { Text((model.pairingPrompt?.name ?? "") + "\n\n" + (model.pairingPrompt?.code ?? "") + "\n\n" + tr("Сверь код на обоих компьютерах.", "Verify the code on both computers.")) }
        .dropDestination(for: URL.self) { urls, _ in guard let url = urls.first else { return false }; model.magnet = url.isFileURL ? url.path : url.absoluteString; model.addTorrent = true; return true }
    }
    private func sidebar(_ entry: Section) -> some View {
        Button { model.select(entry) } label: { HStack { Label(entry.title, systemImage: entry.icon); Spacer(); if entry == .downloads { DownloadBadge(store: model.transfers) } }.padding(.vertical, 5).contentShape(Rectangle()) }.buttonStyle(.plain)
            .listRowBackground(model.section == entry ? model.accent.opacity(0.15) : Color.clear)
            .accessibilityIdentifier("nav-" + entry.rawValue)
    }
    @ViewBuilder private var content: some View {
        switch model.route {
        case .section(.featured): FeaturedView()
        case .section(.movies), .section(.series), .section(.games): CatalogView()
        case .section(.saved): CatalogView(saved: true)
        case .section(.downloads): DownloadsView(store: model.transfers)
        case .section(.settings): SettingsView()
        case .collection(let id): CollectionView(id: id)
        case .detail(let card): DetailView(card: card)
        case .person(let value): PersonView(person: value)
        }
    }
    private func retry() async {
        model.error = nil
        switch model.route { case .detail(let card): await model.loadDetail(card); case .person(let person): await model.loadPerson(person); case .collection(let id): await model.loadCollection(id); case .section(.featured): await model.loadFeatured(); default: model.revision += 1 }
    }
}
struct DownloadBadge: View {
    @ObservedObject var store: TransferStore
    var body: some View { let count = store.items.filter { !$0.paused && !$0.complete }.count; if count > 0 { Text(String(count)).font(.caption.monospacedDigit()).padding(.horizontal, 6).padding(.vertical, 2).background(.quaternary, in: Capsule()) } }
}
struct AddTorrentSheet: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) var dismiss
    @State private var busy = false
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(tr("Добавить торрент", "Add torrent")).font(.title2.bold())
            TextField(tr("Magnet-ссылка или путь к .torrent", "Magnet link or .torrent path"), text: $model.magnet).textFieldStyle(.roundedBorder).frame(minWidth: 440)
            if let error = model.error { Text(error).foregroundStyle(.red).font(.callout) }
            HStack {
                Button(tr("Выбрать файл…", "Choose file…")) { Task { let panel = NSOpenPanel(); panel.allowedContentTypes = [UTType(filenameExtension: "torrent") ?? .data]; if await panel.begin() == .OK, let url = panel.url { model.magnet = url.path } } }
                Spacer(); Button(tr("Отмена", "Cancel")) { dismiss() }.keyboardShortcut(.cancelAction)
                Button(tr("Добавить", "Add")) { busy = true; Task { await model.add(); busy = false } }.buttonStyle(.borderedProminent).disabled(model.magnet.isEmpty || busy).keyboardShortcut(.defaultAction)
            }
            if busy { ProgressView().controlSize(.small) }
        }.padding(24)
    }
}
