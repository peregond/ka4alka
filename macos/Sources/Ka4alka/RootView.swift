import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct RootView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var updater: MacUpdater
    @FocusState private var searchFocused: Bool
    var body: some View {
        GeometryReader { window in
        NavigationSplitView {
            VStack(spacing: 0) {
                HStack(spacing: 10) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable().interpolation(.high).frame(width: 32, height: 32)
                        .accessibilityHidden(true)
                    Text(tr("Качалка", "Ka4alka")).font(.title3.weight(.semibold))
                    Spacer(minLength: 0)
                }.padding(.horizontal, 18).padding(.top, 18).padding(.bottom, 20)
                List {
                    SwiftUI.Section(tr("Медиатека", "Library")) {
                        ForEach(Section.allCases.filter { $0 != .downloads && $0 != .settings }) { entry in sidebar(entry) }
                    }
                    SwiftUI.Section { sidebar(.downloads); sidebar(.settings) }
                }.listStyle(.sidebar)
                VStack(alignment: .leading, spacing: 12) {
                    Divider()
                    if updater.available {
                        Button { Task { await updater.primaryAction() } } label: {
                            Label(updater.ready ? tr("Обновить", "Update") : tr("Скачать", "Download"), systemImage: "arrow.triangle.2.circlepath")
                                .frame(maxWidth: .infinity)
                        }.buttonStyle(.borderedProminent).controlSize(.large)
                            .disabled(updater.busy)
                            .help(updater.buttonTitle).accessibilityLabel(updater.buttonTitle)
                    }
                    Text(tr("Версия", "Version") + " " + model.version)
                        .font(.caption).foregroundStyle(.secondary)
                }.padding(.horizontal, 16).padding(.bottom, 16)
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
                .navigationTitle(navigationTitle)
                .toolbar {
                    ToolbarItem(placement: .navigation) { Button { model.back() } label: { Image(systemName: "chevron.left") }.disabled(model.routes.count < 2).help(tr("Назад", "Back")).accessibilityLabel(tr("Назад", "Back")) }
                    ToolbarItem(placement: .principal) {
                        // Keep room for the sidebar, native title and utility buttons.
                        searchField(width: max(160, min(650, window.size.width - 600)))
                    }
                    ToolbarItemGroup(placement: .primaryAction) {
                        Button { model.addTorrent = true } label: { Image(systemName: "plus") }.help(tr("Добавить торрент", "Add torrent")).accessibilityLabel(tr("Добавить торрент", "Add torrent"))
                            .settingsLayoutProbe("toolbar.add")
                        Button { Task { await model.set("light", .bool(!(model.settings?.light ?? false))) } } label: { Image(systemName: model.settings?.light == true ? "sun.max" : "moon") }.help(tr("Сменить тему", "Change appearance")).accessibilityLabel(tr("Сменить тему", "Change appearance"))
                            .settingsLayoutProbe("toolbar.appearance")
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
    }
    private var navigationTitle: String {
        switch model.route {
        case .section(let section): return section.title
        case .detail(let card): return card.title
        case .person(let person): return person.name
        case .collection: return tr("Подборка", "Collection")
        }
    }
    private func searchField(width: CGFloat) -> some View {
        HStack(spacing: 8) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary).accessibilityHidden(true)
            TextField(tr("Найти фильм, сериал или игру", "Find a movie, series or game"), text: $model.query)
                .textFieldStyle(.plain).focused($searchFocused).onSubmit { model.submit() }
                .frame(minWidth: 0)
                .accessibilityLabel(tr("Поиск в медиатеке", "Search library"))
                .accessibilityIdentifier("library-search")
            if !model.query.isEmpty {
                Button { model.query = ""; if !model.submittedQuery.isEmpty { model.submit() } } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
                }.buttonStyle(.plain).help(tr("Очистить поиск", "Clear search"))
                    .accessibilityLabel(tr("Очистить поиск", "Clear search"))
            }
        }.padding(.horizontal, 11).padding(.vertical, 8)
            .background(Color.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 11))
            .overlay { RoundedRectangle(cornerRadius: 11).strokeBorder(Color.primary.opacity(0.06), lineWidth: 1) }
            // AppKit otherwise uses this principal item's ideal width even in a
            // compact window and sends the entire search and actions to overflow.
            .frame(width: width)
            .settingsLayoutProbe("toolbar.search")
    }
    private func sidebar(_ entry: Section) -> some View {
        let selected = model.section == entry
        return Button { model.select(entry) } label: {
            HStack(spacing: 10) {
                Image(systemName: entry.icon).font(.system(size: 16, weight: .medium))
                    .foregroundStyle(selected ? model.accent : Color.secondary)
                    .frame(width: 22).accessibilityHidden(true)
                Text(entry.title).font(.body.weight(selected ? .semibold : .regular)).foregroundStyle(.primary)
                Spacer(minLength: 4)
                if entry == .downloads { DownloadBadge(store: model.transfers) }
            }.padding(.horizontal, 8).padding(.vertical, 9).contentShape(RoundedRectangle(cornerRadius: 9))
        }.buttonStyle(.plain)
            .listRowInsets(EdgeInsets(top: 2, leading: 8, bottom: 2, trailing: 8))
            .listRowBackground(RoundedRectangle(cornerRadius: 9).fill(selected ? model.accent.opacity(0.15) : Color.clear))
            .accessibilityLabel(entry.title)
            .accessibilityAddTraits(selected ? .isSelected : [])
            .accessibilityIdentifier("nav-" + entry.rawValue)
    }
    @ViewBuilder private var content: some View {
        switch model.route {
        case .section(.featured): FeaturedView()
        case .section(.games) where model.submittedQuery.isEmpty && model.filter == CatalogFilter() && model.page == 1: GameHomeView()
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
