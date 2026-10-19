import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// The three modal sheets of the shell. One state drives them, so only one is ever on screen
/// and the pairing prompt waits until the others are closed.
private enum ShellSheet: Identifiable, Equatable {
    case folder
    case addTorrent
    case pairing(String)
    var id: String {
        switch self {
        case .folder: return "folder"
        case .addTorrent: return "add-torrent"
        case .pairing(let value): return "pairing:" + value
        }
    }
}

/// What the "add torrent" entry points accept: a magnet link, an http(s) link or a .torrent file.
enum ShellTorrentInput {
    static func isValid(_ text: String) -> Bool {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.isEmpty { return false }
        let lower = value.lowercased()
        if lower.hasPrefix("magnet:") { return lower.count > "magnet:".count }
        if lower.hasPrefix("http://") || lower.hasPrefix("https://") {
            guard let url = URL(string: value), let host = url.host else { return false }
            return !host.isEmpty
        }
        var path = value
        if lower.hasPrefix("file://"), let url = URL(string: value), url.isFileURL { path = url.path }
        let expanded = NSString(string: path).expandingTildeInPath
        guard URL(fileURLWithPath: expanded).pathExtension.lowercased() == "torrent" else { return false }
        var isDirectory: ObjCBool = false
        return FileManager.default.fileExists(atPath: expanded, isDirectory: &isDirectory) && !isDirectory.boolValue
    }
    /// A dropped URL is taken only when it is a .torrent file, a magnet link or a web link.
    static func isDroppable(_ url: URL) -> Bool {
        if url.isFileURL { return url.pathExtension.lowercased() == "torrent" }
        let scheme = (url.scheme ?? "").lowercased()
        return scheme == "magnet" || scheme == "http" || scheme == "https"
    }
}

private extension View {
    /// Page colour behind the detail column. On macOS 26 the column stays transparent so the
    /// toolbar's scroll-edge effect can show the content under it.
    @ViewBuilder func shellPageBackground() -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            self
        } else {
            self.background(KDPalette.pageBackground)
        }
        #else
        self.background(KDPalette.pageBackground)
        #endif
    }
}

struct RootView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var updater: MacUpdater
    @Environment(\.colorScheme) private var colorScheme
    @State private var draft = ""
    @State private var dropTargeted = false

    var body: some View {
        NavigationSplitView {
            sidebar
        } detail: {
            detailColumn
        }
        .navigationSplitViewStyle(.balanced)
        .sheet(item: sheetBinding) { sheet in sheetContent(sheet) }
        .dropDestination(for: URL.self, action: { urls, _ in acceptDrop(urls) }, isTargeted: { targeted in dropTargeted = targeted })
        .overlay { dropOverlay }
    }

    // MARK: Sheets

    private var activeSheet: ShellSheet? {
        if model.folderRequired { return ShellSheet.folder }
        if model.addTorrent { return ShellSheet.addTorrent }
        if let prompt = model.pairingPrompt { return ShellSheet.pairing(prompt.id) }
        return nil
    }
    private var sheetBinding: Binding<ShellSheet?> {
        Binding<ShellSheet?>(
            get: { activeSheet },
            set: { next in
                // Only the add sheet can be dismissed by the system. The folder and
                // pairing sheets close when their own buttons answer them.
                if next == nil, activeSheet == ShellSheet.addTorrent { model.addTorrent = false }
            }
        )
    }
    @ViewBuilder private func sheetContent(_ sheet: ShellSheet) -> some View {
        switch sheet {
        case .folder:
            ShellFolderSheet()
        case .addTorrent:
            AddTorrentSheet()
        case .pairing:
            if let prompt = model.pairingPrompt { ShellPairingSheet(prompt: prompt) }
        }
    }

    // MARK: Drag and drop

    private func acceptDrop(_ urls: [URL]) -> Bool {
        guard let url = urls.first(where: { ShellTorrentInput.isDroppable($0) }) else { return false }
        model.magnet = url.isFileURL ? url.path : url.absoluteString
        model.addTorrent = true
        return true
    }
    @ViewBuilder private var dropOverlay: some View {
        if dropTargeted {
            let shape = RoundedRectangle(cornerRadius: KD.Radius.card, style: .continuous)
            ZStack {
                shape.strokeBorder(KDPalette.accent, style: StrokeStyle(lineWidth: 2, dash: [8, 6]))
                Text(tr("Отпусти, чтобы добавить торрент", "Drop to add a torrent"))
                    .font(.headline)
                    .padding(.horizontal, KD.Space.l)
                    .padding(.vertical, KD.Space.s)
                    .kdSurface(in: Capsule())
            }
            .padding(KD.Space.xs)
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }

    // MARK: Sidebar

    private var sidebarSelection: Binding<Section?> {
        Binding<Section?>(
            get: { model.section },
            set: { next in if let next { model.select(next) } }
        )
    }
    private var libraryEntries: [Section] {
        Section.allCases.filter { $0 != .downloads && $0 != .settings }
    }
    private var sidebar: some View {
        List(selection: sidebarSelection) {
            SwiftUI.Section(tr("Медиатека", "Library")) {
                ForEach(libraryEntries) { entry in libraryRow(entry) }
            }
            .collapsible(false)
            SwiftUI.Section {
                ShellDownloadsRow(store: model.transfers, selected: model.section == .downloads)
                    .tag(Section.downloads)
                    .simultaneousGesture(TapGesture().onEnded { revisit(.downloads) })
                sidebarLabel(.settings)
                    .tag(Section.settings)
                    .simultaneousGesture(TapGesture().onEnded { revisit(.settings) })
            }
        }
        .listStyle(.sidebar)
        .settingsLayoutProbe("sidebar")
        .safeAreaInset(edge: .bottom, spacing: 0) { sidebarFooter }
        .navigationSplitViewColumnWidth(min: 170, ideal: 200, max: 240)
    }
    private func sidebarLabel(_ entry: Section) -> some View {
        let symbol = KDNavSymbol.symbol(for: entry.rawValue, selected: model.section == entry)
        return Label(entry.title, systemImage: symbol)
            .accessibilityIdentifier("nav-" + entry.rawValue)
    }
    @ViewBuilder private func libraryRow(_ entry: Section) -> some View {
        if entry == .saved {
            sidebarLabel(entry)
                .badge(model.settings?.savedIds.count ?? 0)
                .tag(entry)
                .simultaneousGesture(TapGesture().onEnded { revisit(entry) })
        } else {
            sidebarLabel(entry)
                .tag(entry)
                .simultaneousGesture(TapGesture().onEnded { revisit(entry) })
        }
    }
    /// Clicking the open section while a title, person or collection is on top goes back to the section page.
    private func revisit(_ entry: Section) {
        if model.section == entry, model.route != Route.section(entry) { model.select(entry) }
    }
    private var showsUpdateStatus: Bool {
        !updater.statusText.isEmpty && (updater.available || updater.busy || updater.isError)
    }
    private var sidebarFooter: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            if updater.available {
                Button { Task { await updater.primaryAction() } } label: {
                    Label(updater.buttonTitle, systemImage: "arrow.triangle.2.circlepath")
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(KDPrimaryButtonStyle())
                .disabled(updater.busy)
                .help(updater.buttonTitle)
            }
            if updater.busy, !updater.ready {
                ProgressView(value: updater.progressValue)
            }
            if showsUpdateStatus {
                Text(updater.statusText)
                    .font(.caption)
                    .foregroundStyle(updater.isError ? KDPalette.danger : Color.secondary)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
            if !model.version.isEmpty {
                Text(tr("Версия", "Version") + " " + model.version)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, KD.Space.m)
        .padding(.vertical, KD.Space.s)
    }

    // MARK: Detail column

    private var detailColumn: some View {
        detailBody
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .shellPageBackground()
            .safeAreaInset(edge: .top, spacing: 0) { errorBanner }
            .kdToast($model.toast)
            .navigationTitle(navigationTitle)
            .toolbar { toolbarContent }
            .searchable(text: $draft, placement: .toolbar, prompt: tr("Найти фильм, сериал или игру", "Find a movie, series or game"))
            .onSubmit(of: .search) { model.query = draft; model.submit() }
            .onChange(of: model.query) { _, next in if draft != next { draft = next } }
            .onChange(of: draft) { _, next in
                if next.isEmpty, !model.submittedQuery.isEmpty { model.query = ""; model.submit() }
            }
    }
    @ViewBuilder private var detailBody: some View {
        if model.connected {
            content
                .id(model.route.key)
                .kdSoftScrollEdge()
        } else if let message = model.error {
            KDEmptyState(
                title: tr("Не удалось запустить движок", "Could not start the engine"),
                systemImage: "exclamationmark.triangle",
                message: message,
                actionTitle: tr("Повторить", "Retry"),
                action: { Task { await model.bootstrap() } }
            )
            .disabled(model.bootstrapping)
        } else {
            ShellStartupView()
        }
    }
    @ViewBuilder private var errorBanner: some View {
        if model.connected, let message = model.error {
            KDBanner(
                message: message,
                actionTitle: tr("Повторить", "Retry"),
                onAction: { Task { await retry() } },
                onDismiss: { model.error = nil }
            )
            .padding(.horizontal, KD.Space.m)
            .padding(.top, KD.Space.xs)
        }
    }
    private var navigationTitle: String {
        switch model.route {
        case .section(let section): return section.title
        case .detail(let card): return card.title
        case .person(let person): return person.name
        case .collection(let id):
            if let title = model.list?.title { return title }
            if let group = model.curated.first(where: { $0.id == id }) { return group.title }
            return tr("Подборка", "Collection")
        }
    }

    // MARK: Toolbar

    private var appearanceSymbol: String { colorScheme == .light ? "sun.max" : "moon" }
    @ToolbarContentBuilder private var toolbarContent: some ToolbarContent {
        if model.routes.count > 1 {
            ToolbarItem(placement: .navigation) {
                KDToolbarButton(title: tr("Назад", "Back"), systemImage: "chevron.backward") { model.back() }
            }
        }
        ToolbarItemGroup(placement: .primaryAction) {
            KDToolbarButton(title: tr("Добавить торрент", "Add torrent"), systemImage: "plus") { model.addTorrent = true }
                .settingsLayoutProbe("toolbar.add")
            KDToolbarButton(title: tr("Обновить каталог", "Refresh catalog"), systemImage: "arrow.clockwise", isDisabled: model.refreshing) { Task { await model.refresh() } }
                .settingsLayoutProbe("toolbar.refresh")
            KDToolbarButton(title: tr("Сменить тему", "Change appearance"), systemImage: appearanceSymbol) { model.setAppearance(colorScheme == .dark ? AppearanceMode.light : AppearanceMode.dark) }
                .settingsLayoutProbe("toolbar.appearance")
        }
    }

    // MARK: Pages

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
        if !model.connected { await model.bootstrap(); return }
        model.error = nil
        switch model.route {
        case .detail(let card): await model.loadDetail(card)
        case .person(let person): await model.loadPerson(person)
        case .collection(let id): await model.loadCollection(id)
        case .section(.featured): await model.loadFeatured(force: true)
        case .section: model.revision += 1
        }
    }
}

/// The Downloads row lives in its own view so the once-a-second queue updates redraw only this row.
struct ShellDownloadsRow: View {
    @ObservedObject var store: TransferStore
    var selected: Bool
    var body: some View {
        let symbol = KDNavSymbol.symbol(for: Section.downloads.rawValue, selected: selected)
        Label(Section.downloads.title, systemImage: symbol)
            .accessibilityIdentifier("nav-" + Section.downloads.rawValue)
            .badge(store.activeCount)
    }
}

/// Shown while the engine starts. After a few seconds it says that the wait is longer than usual.
struct ShellStartupView: View {
    @State private var slow = false
    var body: some View {
        ContentUnavailableView {
            Label(tr("Открываем медиатеку", "Opening your library"), systemImage: "square.grid.2x2")
        } description: {
            VStack(spacing: KD.Space.xxs) {
                Text(tr("Интерфейс готов. Подключаем каталог и очередь загрузок.", "The interface is ready. Connecting the catalog and download queue."))
                if slow { Text(tr("Это занимает дольше обычного", "This is taking longer than usual")) }
            }
        } actions: {
            ProgressView().controlSize(.small)
        }
        .task {
            try? await Task.sleep(for: .seconds(8))
            if !Task.isCancelled { slow = true }
        }
    }
}

/// First run: the person must choose where downloads go before anything else.
struct ShellFolderSheet: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            Image(systemName: "folder.badge.plus")
                .font(.system(size: 40))
                .foregroundStyle(KDPalette.accent)
                .accessibilityHidden(true)
            Text(tr("Куда скачивать файлы?", "Where should downloads be saved?"))
                .font(KDFont.section)
                .accessibilityAddTraits(.isHeader)
            Text(tr("Выбери папку. Внутри мы создадим Ka4alka и будем сохранять туда новые загрузки.", "Choose a folder. We will create a Ka4alka folder inside it for new downloads."))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if let message = model.error {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(KDPalette.danger)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack {
                Button(tr("Выйти", "Quit")) { NSApp.terminate(nil) }
                    .buttonStyle(KDSecondaryButtonStyle())
                Spacer()
                Button(tr("Выбрать папку", "Choose folder")) { Task { await model.chooseFolder() } }
                    .buttonStyle(KDPrimaryButtonStyle())
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(KD.Space.xl)
        .frame(width: 460)
        .interactiveDismissDisabled()
    }
}

/// A device wants to pair. The safe answer, Cancel, is the default action; each button answers exactly once.
struct ShellPairingSheet: View {
    @EnvironmentObject private var model: AppModel
    var prompt: PairingPrompt
    private var spokenCode: String { prompt.code.map { String($0) }.joined(separator: " ") }
    var body: some View {
        VStack(spacing: KD.Space.m) {
            Text(tr("Сопряжение устройств", "Pair devices"))
                .font(KDFont.section)
                .accessibilityAddTraits(.isHeader)
            Text(prompt.name)
                .font(.headline)
                .textSelection(.enabled)
            Text(prompt.code)
                .font(.system(size: 34, weight: .semibold, design: .monospaced))
                .textSelection(.enabled)
                .accessibilityLabel(spokenCode)
            Text(tr("Сверь код на обоих компьютерах.", "Verify the code on both computers."))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: KD.Space.s) {
                Button(tr("Коды совпадают", "Codes match")) { answer(true) }
                    .buttonStyle(KDSecondaryButtonStyle())
                Button(tr("Отмена", "Cancel")) { answer(false) }
                    .buttonStyle(KDPrimaryButtonStyle())
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(KD.Space.xl)
        .frame(width: 420)
        .interactiveDismissDisabled()
        .onExitCommand { answer(false) }
    }
    private func answer(_ accepted: Bool) {
        Task { await model.approvePairing(accepted) }
    }
}

struct AddTorrentSheet: View {
    @EnvironmentObject var model: AppModel
    @FocusState private var fieldFocused: Bool
    @State private var busy = false
    private static let torrentType: UTType = UTType("org.bittorrent.torrent") ?? UTType(filenameExtension: "torrent") ?? UTType.data
    private var trimmed: String { model.magnet.trimmingCharacters(in: .whitespacesAndNewlines) }
    private var canAdd: Bool { !busy && ShellTorrentInput.isValid(trimmed) }
    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            Text(tr("Добавить торрент", "Add torrent"))
                .font(KDFont.section)
                .accessibilityAddTraits(.isHeader)
            TextField(tr("Magnet-ссылка или путь к .torrent", "Magnet link or .torrent path"), text: $model.magnet)
                .textFieldStyle(.roundedBorder)
                .focused($fieldFocused)
                .frame(minWidth: 440)
            if let message = model.addError {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(KDPalette.danger)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack {
                Button(tr("Выбрать файл…", "Choose file…")) { chooseFile() }
                Spacer()
                if busy { ProgressView().controlSize(.small) }
                Button(tr("Отмена", "Cancel")) { cancel() }
                    .keyboardShortcut(.cancelAction)
                Button(tr("Добавить", "Add")) { submit() }
                    .buttonStyle(KDPrimaryButtonStyle())
                    .disabled(!canAdd)
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(KD.Space.l)
        .onAppear { fieldFocused = true }
    }
    private func cancel() {
        model.magnet = ""
        model.addError = nil
        model.addTorrent = false
    }
    private func submit() {
        guard canAdd else { return }
        busy = true
        model.magnet = trimmed
        Task { @MainActor in
            await model.add()
            busy = false
        }
    }
    private func chooseFile() {
        Task { @MainActor in
            let panel = NSOpenPanel()
            panel.allowedContentTypes = [AddTorrentSheet.torrentType]
            panel.allowsMultipleSelection = false
            panel.canChooseDirectories = false
            let response: NSApplication.ModalResponse
            if let window = NSApp.keyWindow {
                response = await panel.beginSheetModal(for: window)
            } else {
                response = await panel.begin()
            }
            if response == .OK, let url = panel.url { model.magnet = url.path }
        }
    }
}
