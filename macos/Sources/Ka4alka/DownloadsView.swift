import AppKit
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Support types

/// Which strip tab is selected. Each transfer belongs to exactly one category.
private enum DownloadsFilter: String, CaseIterable, Identifiable {
    case all, downloading, seeding, paused, errors
    var id: String { rawValue }
    var title: String {
        switch self {
        case .all: return tr("Все", "All")
        case .downloading: return tr("Качаются", "Downloading")
        case .seeding: return tr("Раздаются", "Seeding")
        case .paused: return tr("На паузе", "Paused")
        case .errors: return tr("Ошибки", "Errors")
        }
    }
    static func category(of item: Transfer) -> DownloadsFilter {
        if item.statusKind == "error" { return DownloadsFilter.errors }
        if item.paused { return DownloadsFilter.paused }
        if item.complete { return DownloadsFilter.seeding }
        return DownloadsFilter.downloading
    }
    func matches(_ item: Transfer) -> Bool {
        if self == DownloadsFilter.all { return true }
        return DownloadsFilter.category(of: item) == self
    }
}

private enum DownloadsSheet: Identifiable {
    case files(String)
    case diagnostics(String)
    var id: String {
        switch self {
        case .files(let value): return "files:" + value
        case .diagnostics(let value): return "diagnostics:" + value
        }
    }
}

/// What a row asks the page to do (the page owns the sheets and the confirmations).
enum DownloadsRowRequest {
    case files, diagnostics, remove, deleteFiles
}

/// Ordering that keeps ties stable, so equal rows never swap between refreshes.
private enum DownloadsSorting {
    static func compare(_ a: Transfer, _ b: Transfer, key: String) -> Int {
        switch key {
        case "name":
            let result = a.title.localizedStandardCompare(b.title)
            if result == ComparisonResult.orderedAscending { return -1 }
            if result == ComparisonResult.orderedDescending { return 1 }
        case "speed":
            let x = a.downloadRate + a.uploadRate
            let y = b.downloadRate + b.uploadRate
            if x != y { return x > y ? -1 : 1 }
        case "size":
            let x = a.totalBytes ?? -1
            let y = b.totalBytes ?? -1
            if x != y { return x > y ? -1 : 1 }
        case "progress":
            if a.progress != b.progress { return a.progress > b.progress ? -1 : 1 }
        default:
            break
        }
        if a.addedUtc != b.addedUtc { return a.addedUtc > b.addedUtc ? -1 : 1 }
        if a.id != b.id { return a.id < b.id ? -1 : 1 }
        return 0
    }
    static func sorted(_ items: [Transfer], key: String) -> [Transfer] {
        items.sorted { compare($0, $1, key: key) < 0 }
    }
    static func ids(_ items: [Transfer], key: String) -> [String] {
        sorted(items, key: key).map { $0.id }
    }
}

/// Free space on the volume that holds the download folder.
private struct DownloadsDisk: Equatable {
    var free: Double
    var total: Double
    var used: Double { total > 0 ? min(1, max(0, 1 - free / total)) : 0 }
    static let lowSpaceBytes: Double = 5_000_000_000

    static func read(path: String) -> DownloadsDisk? {
        if path.isEmpty { return nil }
        var url = URL(fileURLWithPath: path)
        let manager = FileManager.default
        while !manager.fileExists(atPath: url.path), url.path != "/" {
            url = url.deletingLastPathComponent()
        }
        let keys: Set<URLResourceKey> = [.volumeAvailableCapacityForImportantUsageKey, .volumeTotalCapacityKey]
        guard let values = try? url.resourceValues(forKeys: keys) else { return nil }
        guard let free = values.volumeAvailableCapacityForImportantUsage, let total = values.volumeTotalCapacity else { return nil }
        if total <= 0 { return nil }
        return DownloadsDisk(free: Double(free), total: Double(total))
    }
}

/// Pieces of text that depend on counts.
enum DownloadsText {
    static func downloadsCount(_ count: Int) -> String {
        if KDFormat.isEnglish { return count == 1 ? "1 download" : String(count) + " downloads" }
        let mod100 = count % 100
        let mod10 = count % 10
        let word: String
        if mod100 >= 11 && mod100 <= 14 { word = "загрузок" }
        else if mod10 == 1 { word = "загрузка" }
        else if mod10 >= 2 && mod10 <= 4 { word = "загрузки" }
        else { word = "загрузок" }
        return String(count) + " " + word
    }
}

/// What the page accepts by drop and paste: .torrent files, magnet links and web links.
enum DownloadsInput {
    static func isDroppable(_ url: URL) -> Bool {
        if url.isFileURL { return url.pathExtension.lowercased() == "torrent" }
        let scheme = (url.scheme ?? "").lowercased()
        return scheme == "magnet" || scheme == "http" || scheme == "https"
    }
    static func source(for url: URL) -> String { url.isFileURL ? url.path : url.absoluteString }
    /// A word of pasted text: a magnet link or a direct link to a .torrent file.
    static func isLink(_ word: String) -> Bool {
        let lower = word.lowercased()
        if lower.hasPrefix("magnet:?") { return true }
        guard lower.hasPrefix("http://") || lower.hasPrefix("https://"), let url = URL(string: word) else { return false }
        return url.pathExtension.lowercased() == "torrent"
    }
    static func clipboardSources() -> [String] {
        var found: [String] = []
        let board = NSPasteboard.general
        if let objects = board.readObjects(forClasses: [NSURL.self], options: nil) {
            for object in objects {
                if let url = object as? URL, isDroppable(url) { found.append(source(for: url)) }
            }
        }
        if let text = board.string(forType: NSPasteboard.PasteboardType.string) {
            for word in text.components(separatedBy: CharacterSet.whitespacesAndNewlines) where isLink(word) {
                found.append(word)
            }
        }
        var seen = Set<String>()
        return found.filter { seen.insert($0).inserted }
    }
}

/// Actions shared by the row and the sheets.
@MainActor enum DownloadsActions {
    static func openCard(for item: Transfer, model: AppModel) {
        guard let id = item.mediaId else { return }
        if let card = model.card(id) {
            model.open(card)
        } else {
            let section: String = id.hasPrefix("games:") ? "games" : (id.hasPrefix("series:") ? "series" : "movies")
            model.open(MediaCard(id: id, title: item.title, section: section, year: 0, rating: "—", poster: item.poster, description: "", genre: "", quality: nil, saved: false, platform: nil))
        }
    }
    static func openFolder(of item: Transfer, model: AppModel) {
        let path = item.folder
        if !path.isEmpty, FileManager.default.fileExists(atPath: path) {
            NSWorkspace.shared.open(URL(fileURLWithPath: path))
        } else {
            model.toast = tr("Папка ещё не создана", "The folder does not exist yet")
        }
    }
}

// MARK: - Page

struct DownloadsView: View {
    @EnvironmentObject private var model: AppModel
    @ObservedObject var store: TransferStore
    @State private var showLimits = false
    @State private var order: [String] = []
    @State private var hovering = false
    @State private var filter: DownloadsFilter = DownloadsFilter.all
    @State private var localSort: String?
    @State private var sheet: DownloadsSheet?
    @State private var removeTarget: Transfer?
    @State private var deleteTarget: Transfer?
    @State private var dropTargeted = false
    @State private var disk: DownloadsDisk?

    // MARK: Derived state

    private var sortKey: String { localSort ?? model.settings?.downloadSort ?? "newest" }
    private var isFrozen: Bool { hovering || sheet != nil || removeTarget != nil || deleteTarget != nil }

    private var displayed: [Transfer] {
        var lookup: [String: Transfer] = [:]
        for item in store.items where lookup[item.id] == nil { lookup[item.id] = item }
        var result: [Transfer] = []
        var seen = Set<String>()
        for id in order {
            if let item = lookup[id], seen.insert(id).inserted { result.append(item) }
        }
        for item in DownloadsSorting.sorted(store.items, key: sortKey) where seen.insert(item.id).inserted {
            result.append(item)
        }
        let active = effectiveFilter
        return result.filter { active.matches($0) }
    }

    private func count(_ tab: DownloadsFilter) -> Int {
        if tab == DownloadsFilter.all { return store.items.count }
        return store.items.filter { tab.matches($0) }.count
    }
    private var hasErrors: Bool { count(DownloadsFilter.errors) > 0 }
    private var effectiveFilter: DownloadsFilter {
        if filter == DownloadsFilter.errors && !hasErrors { return DownloadsFilter.all }
        return filter
    }
    private var visibleTabs: [DownloadsFilter] {
        DownloadsFilter.allCases.filter { $0 != DownloadsFilter.errors || hasErrors }
    }

    private var summaryText: String? {
        if store.items.isEmpty { return nil }
        var parts: [String] = [DownloadsText.downloadsCount(store.items.count)]
        let downloading = count(DownloadsFilter.downloading)
        let seeding = count(DownloadsFilter.seeding)
        let paused = count(DownloadsFilter.paused)
        if downloading > 0 { parts.append(tr("Качаются: \(downloading)", "Downloading: \(downloading)")) }
        if seeding > 0 { parts.append(tr("Раздаются: \(seeding)", "Seeding: \(seeding)")) }
        if paused > 0 { parts.append(tr("На паузе: \(paused)", "Paused: \(paused)")) }
        return parts.joined(separator: " · ")
    }

    private func transfer(_ id: String) -> Transfer? { store.items.first(where: { $0.id == id }) }

    // MARK: Body

    var body: some View {
        observedPage
            .confirmationDialog(
                tr("Удалить из загрузок?", "Remove from downloads?"),
                isPresented: Binding<Bool>(get: { removeTarget != nil }, set: { shown in if !shown { removeTarget = nil } }),
                titleVisibility: Visibility.visible,
                presenting: removeTarget
            ) { target in
                Button(tr("Удалить из загрузок", "Remove from Downloads")) { perform("remove", target, deleteFiles: false) }
                Button(tr("Отмена", "Cancel"), role: .cancel) {}
            } message: { target in
                Text(target.title + "\n" + tr("Скачанные файлы останутся на диске.", "Downloaded files stay on disk."))
            }
            .confirmationDialog(
                tr("Удалить файлы этой загрузки?", "Delete this download’s files?"),
                isPresented: Binding<Bool>(get: { deleteTarget != nil }, set: { shown in if !shown { deleteTarget = nil } }),
                titleVisibility: Visibility.visible,
                presenting: deleteTarget
            ) { target in
                Button(tr("Удалить файлы", "Delete Files"), role: .destructive) { perform("remove", target, deleteFiles: true) }
                Button(tr("Отмена", "Cancel"), role: .cancel) {}
            } message: { target in
                Text(target.title + "\n" + tr("Скачанные и частично скачанные файлы будут удалены без возможности восстановления. Другие файлы в папке останутся.", "Downloaded and partial files will be deleted and cannot be restored. Other files in the folder are kept."))
            }
    }

    private var observedPage: some View {
        content
            .kdPinnedHeader { pinnedHeader }
            .overlay { dropHighlight }
            .dropDestination(for: URL.self, action: { urls, _ in handleDrop(urls) }, isTargeted: { targeted in dropTargeted = targeted })
            .onPasteCommand(of: [UTType.url, UTType.plainText]) { _ in pasteFromClipboard() }
            .onHover { inside in hovering = inside }
            .onAppear { order = DownloadsSorting.ids(store.items, key: sortKey) }
            .onChange(of: store.items) { _, _ in itemsChanged() }
            .onChange(of: sortKey) { _, _ in order = DownloadsSorting.ids(store.items, key: sortKey) }
            .onChange(of: hovering) { _, inside in if !inside { refreshOrder() } }
            .onChange(of: sheet == nil) { _, closed in if closed { refreshOrder() } }
            .task(id: model.settings?.folder ?? "") { await watchDisk() }
            .sheet(item: $sheet) { current in sheetContent(current) }
    }

    @ViewBuilder private var content: some View {
        if store.items.isEmpty {
            emptyState
        } else {
            ScrollView {
                VStack(alignment: .leading, spacing: KD.Space.m) {
                    summaryTiles
                    rows
                }
                .padding(.horizontal, KD.gutter)
                .padding(.vertical, KD.Space.m)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var emptyState: some View {
        KDEmptyState(
            title: tr("Пока нет загрузок", "No downloads yet"),
            systemImage: "arrow.down.circle",
            message: tr("Найди фильм, сериал или игру и выбери раздачу. Или добавь magnet-ссылку либо .torrent-файл: перетащи его сюда или вставь из буфера обмена.", "Find a movie, series or game and choose a release. Or add a magnet link or a .torrent file: drop it here or paste it from the clipboard."),
            actionTitle: tr("Добавить торрент", "Add Torrent"),
            action: { model.addTorrent = true }
        )
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    // MARK: Header

    private var pinnedHeader: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            KDPageHeader(tr("Загрузки", "Downloads"), subtitle: summaryText) { headerActions }
                .settingsLayoutProbe("downloads.header")
            if !store.items.isEmpty { controlStrip }
        }
        .padding(.horizontal, KD.gutter)
        .padding(.top, KD.Space.m)
        .padding(.bottom, KD.Space.s)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var headerActions: some View {
        HStack(spacing: KD.Space.xs) {
            speedButton
            controlsMenu
            addButton
        }
    }

    private var addButton: some View {
        Button { model.addTorrent = true } label: { Label(tr("Добавить торрент", "Add Torrent"), systemImage: "plus") }
            .buttonStyle(KDPrimaryButtonStyle())
            .accessibilityIdentifier("downloads-add")
    }

    private var speedButton: some View {
        KDIconButton(systemImage: "speedometer", label: tr("Ограничения скорости", "Speed limits")) { showLimits = true }
            .popover(isPresented: $showLimits, arrowEdge: .bottom) {
                SpeedLimitsView(onApplied: { showLimits = false })
                    .padding(KD.Space.l)
                    .frame(width: 340)
            }
    }

    private var controlsMenu: some View {
        Menu {
            Button(tr("Запустить загрузки", "Start downloads")) { Task { await model.batch(completed: false, paused: false) } }
                .disabled(store.items.isEmpty)
            Button(tr("Остановить загрузки", "Pause downloads")) { Task { await model.batch(completed: false, paused: true) } }
                .disabled(store.items.isEmpty)
            Divider()
            Button(tr("Запустить раздачи", "Start seeding")) { Task { await model.batch(completed: true, paused: false) } }
                .disabled(store.items.isEmpty)
            Button(tr("Остановить раздачи", "Pause seeding")) { Task { await model.batch(completed: true, paused: true) } }
                .disabled(store.items.isEmpty)
            Divider()
            Button(tr("Ограничения скорости…", "Speed limits…")) { showLimits = true }
        } label: {
            KDChipMenuContent(text: tr("Управление", "Controls"), systemImage: "slider.horizontal.3")
        }
        .kdChipMenu()
        .frame(minHeight: 24)
        .contentShape(Rectangle())
        .fixedSize()
        .accessibilityLabel(tr("Управление загрузками", "Manage downloads"))
        .settingsLayoutProbe("menu.downloads.controls", kind: .control)
    }

    private var controlStrip: some View {
        HStack(alignment: .top, spacing: KD.Space.s) {
            KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
                ForEach(visibleTabs) { tab in tabButton(tab) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            sortPicker
        }
    }

    private func tabButton(_ tab: DownloadsFilter) -> some View {
        let selected = tab == effectiveFilter
        let label = tab.title + " " + String(count(tab))
        return Button { filter = tab } label: { KDChipLabel(text: label, isActive: selected) }
            .buttonStyle(KDSecondaryButtonStyle())
            .accessibilityLabel(label)
            .accessibilityAddTraits(selected ? AccessibilityTraits.isSelected : AccessibilityTraits())
    }

    private var sortBinding: Binding<String> {
        Binding<String>(
            get: { sortKey },
            set: { value in
                localSort = value
                Task {
                    await model.set("downloadSort", .string(value))
                    localSort = nil
                }
            }
        )
    }

    private var sortTitle: String {
        switch sortKey {
        case "name": return tr("По названию", "Name")
        case "speed": return tr("По скорости", "Speed")
        case "size": return tr("По размеру", "Size")
        case "progress": return tr("По прогрессу", "Progress")
        default: return tr("Сначала новые", "Newest first")
        }
    }

    private var sortPicker: some View {
        KDChipMenu(title: tr("Порядок", "Sort"), value: sortTitle) {
            Picker(tr("Порядок", "Sort"), selection: sortBinding) {
                Text(tr("Сначала новые", "Newest first")).tag("newest")
                Text(tr("По названию", "Name")).tag("name")
                Text(tr("По скорости", "Speed")).tag("speed")
                Text(tr("По размеру", "Size")).tag("size")
                Text(tr("По прогрессу", "Progress")).tag("progress")
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
    }

    // MARK: Summary tiles

    private var summaryTiles: some View {
        let columns: [GridItem] = [GridItem(.adaptive(minimum: 190), spacing: KD.Space.s, alignment: .top)]
        return LazyVGrid(columns: columns, alignment: .leading, spacing: KD.Space.s) {
            downloadTile
            uploadTile
            diskTile
        }
        .settingsLayoutProbe("downloads.tiles")
    }

    private var downloadTile: some View {
        DownloadsTile(title: tr("Скачивание", "Download"), systemImage: "arrow.down", value: KDFormat.rate(store.downloadRate), tone: KDTone.accent, share: nil)
    }
    private var uploadTile: some View {
        DownloadsTile(title: tr("Отдача", "Upload"), systemImage: "arrow.up", value: KDFormat.rate(store.uploadRate), tone: KDTone.info, share: nil)
    }
    private var diskTile: some View {
        let low: Bool = (disk?.free ?? DownloadsDisk.lowSpaceBytes) < DownloadsDisk.lowSpaceBytes
        let value: String = disk.map { KDFormat.bytes($0.free) + " " + tr("свободно", "free") } ?? "—"
        return DownloadsTile(title: tr("Диск", "Disk"), systemImage: "internaldrive", value: value, tone: low ? KDTone.warning : KDTone.neutral, share: disk?.used)
    }

    private func watchDisk() async {
        let path = model.settings?.folder ?? ""
        while !Task.isCancelled {
            let reading: DownloadsDisk? = await Task.detached { DownloadsDisk.read(path: path) }.value
            if reading != disk { disk = reading }
            try? await Task.sleep(for: .seconds(30))
        }
    }

    // MARK: Rows

    @ViewBuilder private var rows: some View {
        let shown = displayed
        if shown.isEmpty {
            Text(tr("В этой вкладке пока ничего нет.", "Nothing in this tab yet."))
                .font(KDFont.secondary)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .center)
                .padding(.vertical, KD.Space.l)
        } else {
            LazyVStack(alignment: .leading, spacing: KD.Space.xs) {
                ForEach(Array(shown.enumerated()), id: \.element.id) { index, item in
                    TransferRow(item: item, onRequest: { request, target in handle(request, target) })
                        .settingsLayoutProbe("downloads.row." + String(index))
                }
            }
        }
    }

    private func handle(_ request: DownloadsRowRequest, _ item: Transfer) {
        switch request {
        case .files: sheet = DownloadsSheet.files(item.id)
        case .diagnostics: sheet = DownloadsSheet.diagnostics(item.id)
        case .remove: removeTarget = item
        case .deleteFiles: deleteTarget = item
        }
    }

    private func perform(_ method: String, _ item: Transfer, deleteFiles: Bool) {
        Task { await model.transferAction(method, id: item.id, deleteFiles: deleteFiles) }
    }

    @ViewBuilder private func sheetContent(_ current: DownloadsSheet) -> some View {
        switch current {
        case .files(let id):
            if let item = transfer(id) { TransferFilesView(item: item) }
        case .diagnostics(let id):
            if let item = transfer(id) { TransferDiagnosticsView(item: item) }
        }
    }

    // MARK: Order

    private func itemsChanged() {
        if let current = sheet {
            let id: String
            switch current {
            case .files(let value): id = value
            case .diagnostics(let value): id = value
            }
            if transfer(id) == nil { sheet = nil }
        }
        refreshOrder()
    }

    /// Rows never move under the pointer: while it is over the list (or a dialog is open) only
    /// additions and removals change the order; everything else waits until it leaves.
    private func refreshOrder() {
        if !isFrozen {
            order = DownloadsSorting.ids(store.items, key: sortKey)
            return
        }
        let ids = Set(store.items.map { $0.id })
        let known = Set(order)
        if ids == known { return }
        let kept = order.filter { ids.contains($0) }
        let added = store.items.filter { !known.contains($0.id) }
        order = DownloadsSorting.ids(added, key: sortKey) + kept
    }

    // MARK: Drop and paste

    @ViewBuilder private var dropHighlight: some View {
        if dropTargeted {
            let shape = RoundedRectangle(cornerRadius: KD.Radius.card, style: .continuous)
            shape
                .fill(KDPalette.accentSoft)
                .overlay(shape.strokeBorder(KDPalette.accent, style: StrokeStyle(lineWidth: 2, dash: [8, 6])))
                .padding(KD.Space.xs)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }

    private func handleDrop(_ urls: [URL]) -> Bool {
        let sources = urls.filter { DownloadsInput.isDroppable($0) }.map { DownloadsInput.source(for: $0) }
        if sources.isEmpty { return false }
        addSources(sources)
        return true
    }

    private func pasteFromClipboard() {
        let sources = DownloadsInput.clipboardSources()
        if sources.isEmpty {
            model.toast = tr("В буфере обмена нет magnet-ссылки или .torrent-файла", "The clipboard has no magnet link or .torrent file")
            return
        }
        addSources(sources)
    }

    private func addSources(_ sources: [String]) {
        Task {
            var added = 0
            var failure: String?
            for source in sources {
                let done: Bool = await model.add(source: source)
                if done { added += 1 } else { failure = model.addError }
            }
            if added > 1 && added == sources.count {
                model.toast = tr("Добавлено раздач: \(added)", "Downloads added: \(added)")
            } else if added > 0 && added < sources.count {
                let total: Int = sources.count
                model.toast = tr("Добавлено: \(added) из \(total)", "Added \(added) of \(total)")
            } else if added == 0 {
                let reason: String = failure ?? ""
                model.toast = reason.isEmpty ? tr("Не удалось добавить раздачу", "Could not add the download") : reason
            }
        }
    }
}

// MARK: - Summary tile

private struct DownloadsTile: View {
    var title: String
    var systemImage: String
    var value: String
    var tone: KDTone
    var share: Double?

    var body: some View {
        HStack(alignment: .center, spacing: KD.Space.s) {
            Image(systemName: systemImage)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(tone == KDTone.neutral ? Color.secondary : tone.foreground)
                .frame(width: 30, height: 30)
                .background(tone.background, in: Circle())
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(KDFont.meta).foregroundStyle(.secondary)
                Text(value)
                    .font(Font.title3.weight(.semibold).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                if let share {
                    ProgressView(value: min(1, max(0, share)))
                        .tint(tone == KDTone.neutral ? KDPalette.accent : tone.foreground)
                        .accessibilityHidden(true)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(KD.Space.s)
        .frame(minWidth: 140, maxWidth: .infinity, alignment: .leading)
        .kdCard(radius: KD.Radius.control)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Row

private struct DownloadsStatItem: Identifiable {
    let id: String
    let symbol: String?
    let text: String
}

private struct DownloadsStat: View {
    var item: DownloadsStatItem
    var body: some View {
        HStack(spacing: 3) {
            if let symbol = item.symbol {
                Image(systemName: symbol).font(.system(size: 10, weight: .semibold)).accessibilityHidden(true)
            }
            Text(item.text)
                .font(KDFont.meta.monospacedDigit())
                .lineLimit(1)
                .truncationMode(.tail)
        }
        .foregroundStyle(.secondary)
        .frame(maxWidth: 240, alignment: .leading)
    }
}

struct TransferRow: View {
    @EnvironmentObject private var model: AppModel
    var item: Transfer
    var onRequest: (DownloadsRowRequest, Transfer) -> Void
    @State private var busy = false
    @State private var updating = false

    // MARK: Derived

    private var displayTitle: String { item.title.isEmpty ? item.releaseTitle : item.title }
    private var kind: String { item.statusKind ?? "" }

    private var tone: KDTone {
        if kind == "error" { return KDTone.danger }
        if item.missing || item.lowSpace { return KDTone.warning }
        switch kind {
        case "downloading": return KDTone.accent
        case "waiting": return KDTone.warning
        case "seeding": return KDTone.info
        case "paused", "checking": return KDTone.neutral
        default: break
        }
        if item.complete { return item.paused ? KDTone.neutral : KDTone.success }
        if item.paused { return KDTone.neutral }
        return KDTone.accent
    }

    private var statusSymbol: String? {
        switch kind {
        case "downloading": return "arrow.down"
        case "waiting": return "clock"
        case "seeding": return "arrow.up"
        case "paused": return "pause.fill"
        case "checking": return "magnifyingglass"
        case "error": return "exclamationmark.triangle.fill"
        default: return nil
        }
    }

    private var statusText: String {
        if !item.status.isEmpty { return item.status }
        if item.paused { return tr("На паузе", "Paused") }
        return item.complete ? tr("Готово", "Done") : tr("Скачивается", "Downloading")
    }

    private var stats: [DownloadsStatItem] {
        var rows: [DownloadsStatItem] = []
        if let total = item.totalBytes {
            rows.append(DownloadsStatItem(id: "size", symbol: nil, text: KDFormat.bytes(total)))
        } else {
            rows.append(DownloadsStatItem(id: "size", symbol: nil, text: tr("Размер уточняется", "Size is being determined")))
        }
        if !item.peers.isEmpty {
            rows.append(DownloadsStatItem(id: "peers", symbol: "person.2", text: item.peers))
        }
        let eta: String = item.remaining ?? ""
        if !eta.isEmpty && !item.complete && !item.paused {
            rows.append(DownloadsStatItem(id: "eta", symbol: "clock", text: eta))
        }
        if !item.paused {
            if !item.complete {
                rows.append(DownloadsStatItem(id: "down", symbol: "arrow.down", text: KDFormat.rate(item.downloadRate)))
            }
            rows.append(DownloadsStatItem(id: "up", symbol: "arrow.up", text: KDFormat.rate(item.uploadRate)))
        }
        return rows
    }

    // MARK: Body

    var body: some View {
        HStack(alignment: .top, spacing: KD.Space.s) {
            posterView
            details
            actionButtons
        }
        .padding(KD.Space.s)
        .kdCard(radius: KD.Radius.card)
        .contextMenu { menuItems }
        .accessibilityElement(children: .contain)
    }

    private var details: some View {
        VStack(alignment: .leading, spacing: 6) {
            titleLine
            releaseLine
            progressBar
            statusAndStats
            if item.canUpdateRelease { updateButton }
            if item.missing {
                missingCallout
            } else if !item.hint.isEmpty {
                Text(item.hint).font(KDFont.meta).foregroundStyle(.secondary).lineLimit(2).fixedSize(horizontal: false, vertical: true)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: Poster and title

    private var posterSymbol: String {
        let id = item.mediaId ?? ""
        return id.hasPrefix("games:") ? "gamecontroller" : "film"
    }

    private var posterImage: some View {
        Artwork(address: item.poster, width: 160, placeholderSymbol: posterSymbol)
            .frame(width: KD.Size.rowPoster, height: 72)
            .clipShape(RoundedRectangle(cornerRadius: KD.Radius.thumb, style: .continuous))
    }

    @ViewBuilder private var posterView: some View {
        if item.mediaId != nil {
            Button { DownloadsActions.openCard(for: item, model: model) } label: { posterImage }
                .buttonStyle(.plain)
                .accessibilityLabel(tr("Открыть: ", "Open: ") + displayTitle)
        } else {
            posterImage
        }
    }

    private var titleText: some View {
        Text(displayTitle)
            .font(KDFont.rowTitle)
            .lineLimit(1)
            .truncationMode(.tail)
            .help(displayTitle)
    }

    @ViewBuilder private var titleView: some View {
        if item.mediaId != nil {
            Button { DownloadsActions.openCard(for: item, model: model) } label: { titleText }
                .buttonStyle(.plain)
        } else {
            titleText
        }
    }

    private var titleLine: some View {
        HStack(alignment: .firstTextBaseline, spacing: KD.Space.xs) {
            titleView.frame(maxWidth: .infinity, alignment: .leading)
            if item.indeterminate != true {
                Text(KDFormat.percent(item.progress)).font(KDFont.meta.monospacedDigit()).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder private var releaseLine: some View {
        if !item.releaseTitle.isEmpty && item.releaseTitle != displayTitle {
            Text(item.releaseTitle)
                .font(KDFont.meta)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
                .textSelection(.enabled)
        }
    }

    // MARK: Progress and status

    @ViewBuilder private var progressBar: some View {
        if item.indeterminate == true {
            ProgressView()
                .progressViewStyle(.linear)
                .tint(tone.foreground)
                .accessibilityLabel(displayTitle)
        } else {
            ProgressView(value: min(100, max(0, item.progress)), total: 100)
                .tint(tone.foreground)
                .accessibilityLabel(displayTitle)
                .accessibilityValue(KDFormat.percent(item.progress))
        }
    }

    private var statsWide: some View {
        HStack(spacing: KD.Space.s) {
            ForEach(stats) { stat in DownloadsStat(item: stat) }
        }
    }

    private var statsNarrow: some View {
        KDFlowLayout(spacing: KD.Space.s, lineSpacing: 2) {
            ForEach(stats) { stat in DownloadsStat(item: stat) }
        }
    }

    private var statusAndStats: some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .center, spacing: KD.Space.s) {
                KDStatusPill(statusText, tone: tone, systemImage: statusSymbol)
                statsWide
            }
            VStack(alignment: .leading, spacing: 4) {
                KDStatusPill(statusText, tone: tone, systemImage: statusSymbol)
                statsNarrow
            }
        }
    }

    // MARK: Missing files

    private var missingMessage: String {
        item.hint.isEmpty ? tr("Файлы этой загрузки не найдены на диске.", "The files of this download were not found on disk.") : item.hint
    }

    private var missingCallout: some View {
        let shape = RoundedRectangle(cornerRadius: KD.Radius.control, style: .continuous)
        return VStack(alignment: .leading, spacing: KD.Space.xs) {
            HStack(alignment: .top, spacing: KD.Space.xs) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(KDTone.warning.foreground)
                    .accessibilityHidden(true)
                Text(missingMessage)
                    .font(KDFont.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
            HStack(spacing: KD.Space.xs) {
                Button(tr("Скачать заново", "Download again")) { act("redownload") }
                    .buttonStyle(KDPrimaryButtonStyle())
                    .controlSize(.small)
                Button(tr("Удалить из загрузок", "Remove from Downloads")) { onRequest(DownloadsRowRequest.remove, item) }
                    .buttonStyle(KDSecondaryButtonStyle())
                    .controlSize(.small)
            }
            .disabled(busy)
        }
        .padding(KD.Space.s)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(KDTone.warning.background, in: shape)
        .overlay(shape.strokeBorder(KDTone.warning.foreground.opacity(0.4), lineWidth: 1))
    }

    // MARK: Actions

    private var updateButton: some View {
        Button { updateRelease() } label: {
            HStack(spacing: 6) {
                if updating {
                    ProgressView().controlSize(.small)
                } else {
                    Image(systemName: "arrow.triangle.2.circlepath")
                }
                Text(tr("Обновить раздачу", "Update release"))
            }
        }
        .buttonStyle(KDSecondaryButtonStyle())
        .controlSize(.small)
        .disabled(updating || busy)
        .help(tr("Проверить трекер и заменить раздачу сериала, не теряя скачанные файлы", "Check the tracker and swap the series torrent without losing downloaded files"))
    }

    private var actionButtons: some View {
        HStack(spacing: 2) {
            if !item.missing {
                KDIconButton(
                    systemImage: item.paused ? "play.fill" : "pause.fill",
                    label: item.paused ? tr("Продолжить", "Resume") : tr("Пауза", "Pause"),
                    tone: KDTone.accent
                ) { act("toggle") }
            }
            if item.complete {
                KDIconButton(systemImage: "folder", label: tr("Открыть папку", "Open Folder")) {
                    DownloadsActions.openFolder(of: item, model: model)
                }
            }
            moreMenu
        }
        .disabled(busy)
    }

    private var moreMenu: some View {
        Menu { menuItems } label: {
            Image(systemName: "ellipsis.circle")
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(Color.secondary)
                .frame(width: KD.Size.iconButton, height: KD.Size.iconButton)
                .contentShape(Rectangle())
        }
        .menuStyle(.borderedButton)
        .menuIndicator(.visible)
        .frame(minWidth: KD.Size.iconButton, minHeight: KD.Size.iconButton)
        .contentShape(Rectangle())
        .fixedSize()
        .help(tr("Ещё", "More"))
        .accessibilityLabel(tr("Ещё", "More"))
        .settingsLayoutProbe("menu.downloads.more." + item.id, kind: .control)
    }

    /// No icons: macOS 27 asks for menu icons sparingly, and none of these has a symbol that says more than its label.
    @ViewBuilder private var menuItems: some View {
        Button(tr("Файлы и серии", "Files and Episodes")) { onRequest(DownloadsRowRequest.files, item) }
        Button(tr("Почему не скачивается?", "Why Isn’t It Downloading?")) { onRequest(DownloadsRowRequest.diagnostics, item) }
        Button(tr("Открыть папку", "Open Folder")) { DownloadsActions.openFolder(of: item, model: model) }
        Divider()
        Button(tr("Удалить из загрузок", "Remove from Downloads")) { onRequest(DownloadsRowRequest.remove, item) }
        Button(tr("Удалить файлы…", "Delete Files…"), role: .destructive) { onRequest(DownloadsRowRequest.deleteFiles, item) }
    }

    private func act(_ method: String) {
        if busy { return }
        busy = true
        let id = item.id
        Task {
            await model.transferAction(method, id: id, deleteFiles: false)
            busy = false
        }
    }

    private func updateRelease() {
        if updating { return }
        updating = true
        model.error = nil
        let id = item.id
        Task {
            let swapped: Bool = await model.transferAction("refreshTorrent", id: id, deleteFiles: false)
            updating = false
            if swapped {
                model.toast = tr("Раздача обновлена", "The torrent was updated")
            } else if model.error == nil {
                model.toast = tr("Раздача сериала проверена, новых серий нет", "The series torrent is up to date")
            }
        }
    }
}
