import AppKit
import SwiftUI

struct DownloadsView: View {
    @EnvironmentObject private var model: AppModel
    @ObservedObject var store: TransferStore
    @State private var showLimits = false
    @State private var sort = "newest"
    private var ordered: [Transfer] {
        switch sort { case "name": return store.items.sorted { $0.title.localizedStandardCompare($1.title) == .orderedAscending }; case "speed": return store.items.sorted { $0.downloadRate > $1.downloadRate }; case "size": return store.items.sorted { $0.totalBytes > $1.totalBytes }; case "progress": return store.items.sorted { $0.progress > $1.progress }; default: return store.items.sorted { $0.addedUtc > $1.addedUtc } }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(tr("Очередь", "Queue")).font(.largeTitle.bold()); Spacer()
                Menu {
                    Button(tr("Запустить загрузки", "Start downloads")) { Task { await model.batch(completed: false, paused: false) } }
                    Button(tr("Остановить загрузки", "Pause downloads")) { Task { await model.batch(completed: false, paused: true) } }
                    Divider()
                    Button(tr("Запустить раздачи", "Start seeding")) { Task { await model.batch(completed: true, paused: false) } }
                    Button(tr("Остановить раздачи", "Pause seeding")) { Task { await model.batch(completed: true, paused: true) } }
                    Divider(); Button(tr("Ограничения скорости…", "Speed limits…")) { showLimits = true }
                } label: { Label(tr("Управление", "Controls"), systemImage: "slider.horizontal.3") }
                Picker(tr("Порядок", "Sort"), selection: $sort) { Text(tr("Сначала новые", "Newest first")).tag("newest"); Text(tr("По названию", "Name")).tag("name"); Text(tr("По скорости", "Speed")).tag("speed"); Text(tr("По размеру", "Size")).tag("size"); Text(tr("По прогрессу", "Progress")).tag("progress") }.labelsHidden().frame(width: 170)
            }.padding(.horizontal, 22).padding(.top, 22)
            Text(tr("Всего", "Total") + ": \(store.items.count)  ·  " + tr("Загружается", "Downloading") + ": \(store.items.filter { !$0.paused && !$0.complete }.count)  ·  " + tr("Раздаётся", "Seeding") + ": \(store.items.filter { !$0.paused && $0.complete }.count)").font(.caption).foregroundStyle(.secondary).padding(.horizontal, 22)
            if store.items.isEmpty { ContentUnavailableView(tr("Пока нет загрузок", "No downloads yet"), systemImage: "arrow.down.circle", description: Text(tr("Найди фильм, сериал или игру и выбери раздачу.", "Find a movie, series or game and choose a release."))) }
            else { List(ordered) { item in TransferRow(item: item).listRowSeparator(.hidden).listRowBackground(Color.clear).padding(.vertical, 4) }.listStyle(.inset) }
        }.task { sort = model.settings?.downloadSort ?? "newest" }.onChange(of: sort) { _, value in Task { await model.set("downloadSort", .string(value)) } }.popover(isPresented: $showLimits) { SpeedLimitsView().padding(20).frame(width: 340) }
    }
}
struct TransferRow: View {
    @EnvironmentObject private var model: AppModel
    var item: Transfer
    @State private var details = false
    @State private var diagnostics = false
    @State private var deleteFiles = false
    @State private var busy = false
    var body: some View {
        HStack(alignment: .top, spacing: 13) {
            Button { openCard() } label: { Artwork(address: item.poster, width: 160).frame(width: 48, height: 72).clipShape(RoundedRectangle(cornerRadius: 7)) }.buttonStyle(.plain).disabled(item.mediaId == nil)
            VStack(alignment: .leading, spacing: 5) {
                HStack { Button { openCard() } label: { Text(item.title).font(.body.weight(.semibold)).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading) }.buttonStyle(.plain).disabled(item.mediaId == nil); Text(String(format: "%.1f%%", item.progress)).font(.caption.monospacedDigit()).foregroundStyle(.secondary) }
                if item.releaseTitle != item.title { Text(item.releaseTitle).font(.caption).foregroundStyle(.secondary).lineLimit(1).textSelection(.enabled) }
                ProgressView(value: min(100, max(0, item.progress)), total: 100).tint(item.missing ? .orange : model.accent)
                HStack(spacing: 10) {
                    Text(item.status).font(.caption).foregroundStyle(item.missing ? .orange : .secondary)
                    if !item.paused { Text("↓ " + byteText(item.downloadRate) + "/s   ↑ " + byteText(item.uploadRate) + "/s").font(.caption.monospacedDigit()).foregroundStyle(.secondary) }
                    Spacer()
                    if item.missing { Button(tr("Скачать заново", "Download again")) { act("redownload") }.controlSize(.small) }
                    else { Button(item.paused ? tr("Продолжить", "Resume") : tr("Пауза", "Pause")) { act("toggle") }.controlSize(.small) }
                    Button { details = true } label: { Image(systemName: "list.bullet.rectangle") }.help(tr("Файлы и серии", "Files and episodes"))
                    Button { diagnostics = true } label: { Image(systemName: "info.circle") }.help(tr("Почему не скачивается?", "Why is it not downloading?"))
                    Button { NSWorkspace.shared.open(URL(fileURLWithPath: item.folder)) } label: { Image(systemName: "folder") }.help(tr("Открыть папку", "Open folder"))
                    if item.canUpdateRelease { Button { act("refreshTorrent") } label: { Image(systemName: "arrow.clockwise") }.help(tr("Обновить раздачу сериала", "Update series torrent")) }
                    Button { act("remove") } label: { Image(systemName: "minus.circle").foregroundStyle(.secondary) }.help(tr("Удалить из загрузок", "Remove from downloads"))
                    Button { deleteFiles = true } label: { Image(systemName: "trash").foregroundStyle(.red) }.help(tr("Удалить файлы", "Delete files"))
                }.buttonStyle(.borderless).disabled(busy)
                if !item.hint.isEmpty { Text(item.hint).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
            }
        }.padding(12).background(Color.primary.opacity(0.035), in: RoundedRectangle(cornerRadius: 12))
            .sheet(isPresented: $details) { TransferFilesView(item: item) }
            .sheet(isPresented: $diagnostics) { TransferDiagnosticsView(item: item) }
            .confirmationDialog(tr("Удалить файлы этой загрузки?", "Delete this download’s files?"), isPresented: $deleteFiles, titleVisibility: .visible) { Button(tr("Удалить файлы", "Delete files"), role: .destructive) { act("remove", delete: true) }; Button(tr("Отмена", "Cancel"), role: .cancel) {} } message: { Text(item.title) }
    }
    private func act(_ method: String, delete: Bool = false) { busy = true; Task { await model.transferAction(method, id: item.id, deleteFiles: delete); busy = false } }
    private func openCard() {
        guard let id = item.mediaId else { return }
        if let card = model.card(id) { model.open(card) }
        else { model.open(MediaCard(id: id, title: item.title, section: id.hasPrefix("games:") ? "games" : id.hasPrefix("series:") ? "series" : "movies", year: 0, rating: "—", poster: item.poster, description: "", genre: "", quality: nil, saved: false, platform: nil)) }
    }
}
struct TransferFilesView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) var dismiss
    var item: Transfer
    @State private var files: [DownloadFile] = []
    @State private var error: String?
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack { Text(item.title).font(.title2.bold()); Spacer(); Button(tr("Готово", "Done")) { dismiss() }.keyboardShortcut(.cancelAction) }
            if let error { Text(error).foregroundStyle(.red) }
            if files.isEmpty { ContentUnavailableView(tr("Получаем список файлов", "Waiting for file metadata"), systemImage: "doc.text.magnifyingglass") }
            else { List(files) { file in HStack { VStack(alignment: .leading) { Text(file.name).lineLimit(2); Text(byteText(file.size)).font(.caption).foregroundStyle(.secondary); ProgressView(value: min(100, max(0, file.progress)), total: 100) }; Text(String(format: "%.0f%%", file.progress)).font(.caption.monospacedDigit()); Button { let url = URL(fileURLWithPath: file.fullPath); NSWorkspace.shared.activateFileViewerSelecting([url]) } label: { Image(systemName: "folder") }.buttonStyle(.borderless) } } }
        }.padding(22).frame(minWidth: 600, minHeight: 400).task { do { files = try await model.bridge.call("files", ["id": .string(item.id)], as: FilesResult.self).files } catch { self.error = error.localizedDescription } }
    }
}
struct TransferDiagnosticsView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) var dismiss
    var item: Transfer
    @State private var result: JSONValue?
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack { Text(tr("Почему не скачивается?", "Why is it not downloading?")).font(.title2.bold()); Spacer(); Button(tr("Готово", "Done")) { dismiss() } }
            Text(item.title).foregroundStyle(.secondary)
            if let result { Text(tr(result["diagnosis"]["title"].string)).font(.headline); Text(tr(result["diagnosis"]["explanation"].string)); Text(tr(result["diagnosis"]["recommendation"].string)).foregroundStyle(.secondary); if result["diagnosis"]["canRetryConnections"].bool { Button(tr("Повторить соединение", "Retry connections")) { Task { await model.transferAction("retry", id: item.id); dismiss() } } }; Button(tr("Скопировать логи", "Copy logs")) { Task { await model.logs() } } }
            else { ProgressView() }
        }.padding(24).frame(width: 520).task { result = try? await model.bridge.call("diagnostics", ["id": .string(item.id)]) }
    }
}
struct SpeedLimitsView: View {
    @EnvironmentObject var model: AppModel
    @State private var down = 0
    @State private var up = 0
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(tr("Ограничения скорости", "Speed limits")).font(.headline)
            limitRow(tr("Загрузка, КБ/с", "Download, KB/s"), value: $down)
            limitRow(tr("Отдача, КБ/с", "Upload, KB/s"), value: $up)
            Text(tr("0 — без ограничений", "0 means unlimited")).font(.caption).foregroundStyle(.secondary)
            Button(tr("Применить", "Apply")) { apply() }.buttonStyle(.borderedProminent)
        }.frame(maxWidth: .infinity, alignment: .leading).task { down = model.settings?.maxDownloadKbps ?? 0; up = model.settings?.maxUploadKbps ?? 0 }
    }
    private func limitRow(_ title: String, value: Binding<Int>) -> some View {
        HStack(spacing: 16) {
            Text(title).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            TextField(title, value: value, format: .number).labelsHidden().textFieldStyle(.roundedBorder)
                .font(.body.monospacedDigit()).frame(width: 110).onSubmit { apply() }
        }
    }
    private func apply() { Task { await model.set("maxDownloadKbps", .number(Double(max(0, down)))); await model.set("maxUploadKbps", .number(Double(max(0, up)))) } }
}
