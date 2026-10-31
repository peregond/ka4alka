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

/// What the "add torrent" entry points accept: a magnet link or an existing .torrent file.
enum ShellTorrentInput {
    /// The text as the engine receives it: trimmed, a file:// link turned into a path and `~` expanded.
    /// Magnet links are returned unchanged.
    static func normalized(_ text: String) -> String {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.lowercased().hasPrefix("magnet:") { return value }
        var path = value
        if value.lowercased().hasPrefix("file://"), let url = URL(string: value), url.isFileURL { path = url.path }
        return NSString(string: path).expandingTildeInPath
    }
    static func isValid(_ text: String) -> Bool {
        let value = normalized(text)
        if value.isEmpty { return false }
        if value.lowercased().hasPrefix("magnet:") { return value.count > "magnet:".count }
        guard URL(fileURLWithPath: value).pathExtension.lowercased() == "torrent" else { return false }
        var isDirectory: ObjCBool = false
        return FileManager.default.fileExists(atPath: value, isDirectory: &isDirectory) && !isDirectory.boolValue
    }
    /// A dropped URL is taken only when it is a .torrent file or a magnet link.
    static func isDroppable(_ url: URL) -> Bool {
        if url.isFileURL { return url.pathExtension.lowercased() == "torrent" }
        return (url.scheme ?? "").lowercased() == "magnet"
    }
}

private extension View {
    /// Page colour behind the detail column. It also fills the safe area, so the toolbar's
    /// scroll-edge effect blends with the same near-black page in the dark theme.
    @ViewBuilder func shellPageBackground() -> some View {
        self.background(KDPalette.pageBackground.ignoresSafeArea())
    }
}

struct RootView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var updater: MacUpdater
    @State private var dropTargeted = false
    /// Height of the sidebar column; it sets how many download rows the widget can show.
    @State private var sidebarHeight: CGFloat = 0
    /// The strip under the toolbar in the content column, for banners that run up under it.
    @State private var safeTop: CGFloat = 0
    /// True while the page on screen opens with a full-bleed banner.
    @State private var heroPage = false

    var body: some View {
        NavigationSplitView(columnVisibility: $model.sidebarVisibility) {
            sidebar
                .toolbar(removing: .sidebarToggle)
                // The toggle is an item of the sidebar column, as the system's own is: it sits in the sidebar's header
                // and, once the sidebar is hidden, stays at the leading edge of the window toolbar. It is a control of
                // its own and does not share a glass group with Back.
                .toolbar { ToolbarItem(placement: .primaryAction) { sidebarToggle } }
        } detail: {
            detailColumn
        }
        .navigationSplitViewStyle(.balanced)
        .toolbar(removing: .sidebarToggle)
        .buttonStyle(KDSecondaryButtonStyle())
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
        let matches: [URL] = urls.filter { ShellTorrentInput.isDroppable($0) }
        guard let first = matches.first else { return false }
        if matches.count == 1 {
            model.magnet = DownloadsInput.source(for: first)
            model.addTorrent = true
            return true
        }
        let sources: [String] = matches.map { DownloadsInput.source(for: $0) }
        Task { @MainActor in
            var failed = 0
            for source in sources {
                let added: Bool = await model.add(source: source)
                if !added { failed += 1 }
            }
            if failed > 0, let message = model.addError { model.toast = message }
        }
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

    /// Shows or hides the sidebar.
    private var sidebarToggle: some View {
        Button {
            model.toggleSidebar()
        } label: {
            Label(tr("Боковое меню", "Sidebar"), systemImage: "sidebar.left")
                // The glass capsule is as tall as the toolbar and as wide as its content plus padding; a wide enough
                // content makes it a circle.
                .frame(width: 26, height: 26)
        }
        .labelStyle(.iconOnly)
        .buttonStyle(.automatic)
        // A round button, as the system's own sidebar toggle: without a shape it took the width of its glyph and the
        // height of the toolbar, which drew a tall oval.
        .buttonBorderShape(.circle)
        .controlSize(.regular)
        .help(tr("Показать или скрыть боковое меню", "Show or Hide Sidebar"))
        .accessibilityIdentifier("shell-sidebar-toggle")
        .settingsLayoutProbe("toolbar.sidebar")
    }

    /// Search and the library sections that browse content. Downloads and Settings sit at the bottom.
    private var libraryEntries: [Section] {
        Section.allCases.filter { $0 != .downloads && $0 != .settings }
    }
    /// Every sidebar entry is a plain button that opens its section. The column is a plain stack with the footer
    /// at the bottom, not a scroll view with a safe-area inset: that arrangement drew the rows lower than the
    /// area that receives the click, so a click on one entry opened another. Clicking the open section goes back
    /// to its page when a title, person or collection is on top, and otherwise resets its search, filter and page.
    /// Downloads and Settings sit at the bottom, as in the Windows app, and the live downloads widget stands in for
    /// the Downloads button while something is queued. The column has no fill of its own: the system draws the
    /// sidebar material.
    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: KD.Space.xxs) {
                ShellSearchRow(selected: model.routes.contains(.search)) { model.openSearch() }
                ForEach(libraryEntries) { (entry: Section) in
                    ShellPinnedRow(
                        section: entry,
                        selected: !model.routes.contains(.search) && model.section == entry,
                        store: nil,
                        badge: entry == .saved ? (model.settings?.savedIds.count ?? 0) : 0
                    ) { pick(entry) }
                }
            }
            .padding(.horizontal, KD.Space.s)
            .padding(.top, KD.Space.xxs)
            Spacer(minLength: 0)
            sidebarFooter
        }
        .frame(minWidth: 196, idealWidth: 210, maxWidth: 250, maxHeight: .infinity, alignment: .topLeading)
        .kdReadHeight($sidebarHeight)
        .settingsLayoutProbe("sidebar")
        .navigationSplitViewColumnWidth(min: 196, ideal: 210, max: 250)
    }
    private var showsUpdateStatus: Bool {
        // Progress lives inside the button; keep failures visible beside it.
        !updater.statusText.isEmpty && (updater.isError || (!updater.busy && !updater.ready && updater.available))
    }
    /// The widget is shown only when there is room: its rows follow the height of the column, as on Windows.
    private var downloadWidgetRows: Int {
        if sidebarHeight >= 760 { return 3 }
        if sidebarHeight >= 640 { return 2 }
        if sidebarHeight >= 560 { return 1 }
        return 0
    }
    /// Downloads (or its live widget) above Settings. Plain buttons, not list rows, so a click always reaches them.
    private var pinnedSections: some View {
        VStack(alignment: .leading, spacing: KD.Space.xxs) {
            ShellDownloadsEntry(
                store: model.transfers,
                selected: !model.routes.contains(.search) && model.section == .downloads,
                rows: downloadWidgetRows
            ) { pick(.downloads) }
            ShellPinnedRow(section: .settings, selected: model.section == .settings, store: nil) {
                pick(.settings)
            }
        }
    }
    private func pick(_ entry: Section) {
        model.select(entry)
    }
    private var sidebarFooter: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            pinnedSections
            if updater.available {
                MacUpdateButton(updater: updater, expanded: true, probeID: "sidebar.update.action")
            }
            if showsUpdateStatus {
                Text(updater.statusText)
                    .font(.caption)
                    .foregroundStyle(updater.isError ? KDPalette.danger : Color.secondary)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
                    .settingsLayoutProbe("sidebar.update.status")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, KD.Space.m)
        .padding(.vertical, KD.Space.s)
    }

    // MARK: Detail column

    private var detailColumn: some View {
        detailBody
            .environment(\.kdSafeTop, safeTop)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .settingsLayoutProbe("shell.detail")
            .kdReadSafeTop($safeTop)
            .shellPageBackground()
            .safeAreaInset(edge: .top, spacing: 0) { errorBanner }
            .kdToast($model.toast)
            .onPreferenceChange(KDHeroPageKey.self) { next in if heroPage != next { heroPage = next } }
            .modifier(ShellChromeView(title: navigationTitle, heroPage: heroPage))
    }
    @ViewBuilder private var detailBody: some View {
        if model.connected {
            // The scroll-edge effect keeps the system's automatic style: it is more opaque under toolbars with
            // several controls and over the pinned page headers, which a forced soft edge left hard to read.
            content
                .id(model.route.key)
                .kdSoftScrollEdge(heroPage)
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
        case .search: return tr("Поиск", "Search")
        case .section(let section): return section.title
        case .detail(let card): return card.title
        case .person(let person): return person.name
        case .collection(let id):
            if let title = model.list?.title { return title }
            if let group = model.curated.first(where: { $0.id == id }) { return group.title }
            return tr("Подборка", "Collection")
        }
    }

    // MARK: Pages

    @ViewBuilder private var content: some View {
        switch model.route {
        case .search: SearchView()
        case .section(.featured): FeaturedView()
        case .section(.games) where model.submittedQuery.isEmpty && model.filter == CatalogFilter() && model.page == 1: GameHomeView()
        case .section(.movies), .section(.series), .section(.shows), .section(.games): CatalogView()
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
        case .collection: model.revision += 1
        case .section(.featured): await model.loadFeatured(force: true)
        case .section, .search: model.revision += 1
        }
    }
}

/// The look every sidebar row shares, as in the system's own sidebars: the icon wears the accent colour (the one
/// chosen in System Settings, or the app green), the label stays primary, and the open row gets a neutral highlight.
private struct ShellSidebarRowLook: ViewModifier {
    var selected: Bool
    @Environment(\.colorSchemeContrast) private var contrast
    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: KD.Radius.chip, style: .continuous)
        content
            .foregroundStyle(Color.primary)
            .padding(.horizontal, KD.Space.s)
            .frame(height: 32)
            .background(shape.fill(selected ? Color.primary.opacity(contrast == .increased ? 0.2 : 0.1) : Color.clear))
            .contentShape(shape)
    }
}

struct ShellSearchRow: View {
    var selected: Bool
    var action: () -> Void
    var body: some View {
        Button(action: action) {
            HStack(spacing: KD.Space.xs) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 14, weight: .regular))
                    .foregroundStyle(KDPalette.accent)
                    .frame(width: 20)
                    .accessibilityHidden(true)
                Text(tr("Поиск", "Search")).lineLimit(1)
                Spacer(minLength: 0)
            }
            .modifier(ShellSidebarRowLook(selected: selected))
        }
        .buttonStyle(.plain)
        .help(tr("Поиск (⌘F)", "Search (⌘F)"))
        .accessibilityAddTraits(selected ? .isSelected : AccessibilityTraits())
        .accessibilityIdentifier("nav-search")
    }
}

/// A sidebar button for one section. Downloads shows the number of active transfers; the count lives in
/// its own view so the once-a-second queue updates redraw only that badge.
struct ShellPinnedRow: View {
    var section: Section
    var selected: Bool
    var store: TransferStore?
    var badge: Int = 0
    var action: () -> Void
    var body: some View {
        let symbol: String = KDNavSymbol.symbol(for: section.rawValue, selected: selected)
        return Button(action: action) {
            HStack(spacing: KD.Space.xs) {
                Image(systemName: symbol)
                    .font(.system(size: 14, weight: .regular))
                    .foregroundStyle(section == .saved && selected ? KDPalette.like : KDPalette.accent)
                    .frame(width: 20)
                    .accessibilityHidden(true)
                Text(section.title)
                    .lineLimit(1)
                Spacer(minLength: 0)
                if let store { ShellActiveBadge(store: store) }
                if badge > 0 { ShellCountText(count: badge) }
            }
            .modifier(ShellSidebarRowLook(selected: selected))
        }
        .buttonStyle(.plain)
        .accessibilityLabel(section.title)
        .accessibilityAddTraits(selected ? AccessibilityTraits.isSelected : AccessibilityTraits())
        .accessibilityIdentifier("nav-" + section.rawValue)
    }
}

/// The Downloads entry of the sidebar: the live widget while something is queued and there is room for it, the
/// plain Downloads button otherwise, so there is always exactly one way to the page. It observes the queue, so the
/// once-a-second progress ticks redraw this view and nothing else.
struct ShellDownloadsEntry: View {
    @ObservedObject var store: TransferStore
    var selected: Bool
    var rows: Int
    var open: () -> Void
    var body: some View {
        if !selected, rows > 0, !store.items.isEmpty {
            ShellDownloadsWidget(store: store, rows: rows, open: open)
        } else {
            ShellPinnedRow(section: .downloads, selected: selected, store: store, action: open)
        }
    }
}

/// Active downloads at a glance: the count, both speeds and the first few transfers with their progress.
struct ShellDownloadsWidget: View {
    @ObservedObject var store: TransferStore
    var rows: Int
    var open: () -> Void
    /// Downloading first, then checking, waiting, seeding and paused; the newest first within each group.
    private static func rank(_ item: Transfer) -> Int {
        if item.paused { return 4 }
        switch item.statusKind ?? "" {
        case "downloading": return 0
        case "checking": return 1
        case "waiting": return 2
        case "seeding": return 3
        default: return item.isActive ? 0 : 5
        }
    }
    private var top: [Transfer] {
        Array(store.items.sorted { left, right in
            let a = ShellDownloadsWidget.rank(left), b = ShellDownloadsWidget.rank(right)
            return a != b ? a < b : left.addedUtc > right.addedUtc
        }.prefix(rows))
    }
    var body: some View {
        Button(action: open) {
            VStack(alignment: .leading, spacing: KD.Space.xs) {
                HStack(spacing: 6) {
                    Text(tr("Загрузки", "Downloads")).font(.subheadline.weight(.semibold))
                    if store.activeCount > 0 { ShellCountText(count: store.activeCount) }
                    Spacer(minLength: 0)
                    HStack(spacing: 2) {
                        Text(tr("Все", "All"))
                        Image(systemName: "chevron.forward").font(.system(size: 9, weight: .bold)).accessibilityHidden(true)
                    }
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                }
                HStack(spacing: KD.Space.s) {
                    Text("↓ " + KDFormat.rate(store.downloadRate)).foregroundStyle(KDPalette.accent)
                    Text("↑ " + KDFormat.rate(store.uploadRate)).foregroundStyle(.secondary)
                }
                .font(.caption.monospacedDigit())
                .lineLimit(1)
                ForEach(top) { item in
                    VStack(alignment: .leading, spacing: 3) {
                        Text(item.title.isEmpty ? item.releaseTitle : item.title)
                            .font(.caption)
                            .lineLimit(1)
                        HStack(spacing: 6) {
                            ProgressView(value: min(1, max(0, item.progress / 100)))
                                .progressViewStyle(.linear)
                                .tint(item.paused ? Color.secondary : KDPalette.accent)
                            Text(KDFormat.percent(item.progress))
                                .font(.caption2.monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .padding(KD.Space.s)
            .frame(maxWidth: .infinity, alignment: .leading)
            // A plain tile set into the sidebar, with no outline and no glass: it belongs to the interface, not above it.
            .kdCard(radius: KD.Radius.control)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(tr("Открыть загрузки", "Open Downloads"))
        .accessibilityLabel(tr("Открыть загрузки", "Open Downloads"))
        .accessibilityIdentifier("nav-downloads")
        .settingsLayoutProbe("sidebar.downloads.widget", kind: .control)
    }
}

struct ShellActiveBadge: View {
    @ObservedObject var store: TransferStore
    var body: some View {
        if store.activeCount > 0 {
            ShellCountText(count: store.activeCount)
                .accessibilityLabel(tr("Активных загрузок: ", "Active downloads: ") + String(store.activeCount))
        }
    }
}

struct ShellCountText: View {
    var count: Int
    var body: some View {
        Text(String(count))
            .font(.caption.weight(.semibold))
            .monospacedDigit()
            .foregroundStyle(Color.secondary)
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
                Button(tr("Выбрать папку", "Choose Folder")) { Task { await model.chooseFolder() } }
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
            Text(tr("Сопряжение устройств", "Pair Devices"))
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
                Button(tr("Коды совпадают", "Codes Match")) { answer(true) }
                    .buttonStyle(KDSecondaryButtonStyle())
                Button(tr("Отмена", "Cancel")) { answer(false) }
                    .buttonStyle(KDPrimaryButtonStyle())
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(KD.Space.xl)
        .frame(width: 420)
        .background {
            Button("") { answer(false) }
                .keyboardShortcut(.cancelAction)
                .opacity(0)
                .accessibilityHidden(true)
        }
        .interactiveDismissDisabled()
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
    private var inputHint: String {
        tr("Нужна magnet-ссылка или существующий файл .torrent", "Enter a magnet link or an existing .torrent file")
    }
    private var showsInputHint: Bool { !busy && !trimmed.isEmpty && !ShellTorrentInput.isValid(trimmed) }
    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            Text(tr("Добавить торрент", "Add Torrent"))
                .font(KDFont.section)
                .accessibilityAddTraits(.isHeader)
            TextField(tr("Magnet-ссылка или путь к .torrent", "Magnet link or .torrent path"), text: $model.magnet)
                .textFieldStyle(.roundedBorder)
                .focused($fieldFocused)
                .frame(minWidth: 440)
            if showsInputHint {
                Text(inputHint)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let message = model.addError {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(KDPalette.danger)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack {
                Button(tr("Выбрать файл…", "Choose File…")) { chooseFile() }
                    .disabled(busy)
                Spacer()
                if busy { ProgressView().controlSize(.small) }
                Button(tr("Отмена", "Cancel")) { cancel() }
                    .disabled(busy)
                    .keyboardShortcut(.cancelAction)
                Button(tr("Добавить", "Add")) { submit() }
                    .buttonStyle(KDPrimaryButtonStyle())
                    .disabled(!canAdd)
                    .accessibilityHint(showsInputHint ? inputHint : "")
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
        model.magnet = ShellTorrentInput.normalized(trimmed)
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
