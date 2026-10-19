import AppKit
import SwiftUI

// MARK: - JSON helpers (the engine sends null for unknown values)

private func downloadsNumber(_ value: JSONValue) -> Double? {
    if case .number(let number) = value { return number }
    return nil
}

private func downloadsBool(_ value: JSONValue) -> Bool? {
    if case .bool(let flag) = value { return flag }
    return nil
}

// MARK: - Files sheet

struct TransferFilesView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) var dismiss
    var item: Transfer
    @State private var files: [DownloadFile] = []
    @State private var loaded = false
    @State private var error: String?
    @State private var query = ""
    @FocusState private var searchFocused: Bool

    private var filtered: [DownloadFile] {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { return files }
        return files.filter { $0.name.localizedCaseInsensitiveContains(text) }
    }

    private var summaryText: String {
        let ready = files.filter { $0.progress >= 100 }.count
        var total = 0.0
        var done = 0.0
        for file in files {
            total += max(0, file.size)
            done += max(0, file.size) * min(100, max(0, file.progress)) / 100
        }
        let doneText = KDFormat.bytes(done)
        let totalText = KDFormat.bytes(total)
        let readyLine = tr("Готово файлов \(ready) из \(files.count)", "Files ready: \(ready) of \(files.count)")
        let sizeLine = tr("Скачано \(doneText) из \(totalText)", "Downloaded \(doneText) of \(totalText)")
        return readyLine + " · " + sizeLine
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            header
            if !files.isEmpty {
                Text(summaryText).font(KDFont.secondary).foregroundStyle(.secondary)
            }
            if files.count > 8 { searchField }
            if let error {
                KDBanner(message: error, tone: KDTone.danger, actionTitle: tr("Повторить", "Retry"), onAction: { Task { await load() } })
            }
            listArea
        }
        .padding(KD.Space.l)
        .frame(minWidth: 620, minHeight: 440)
        .task { await poll() }
    }

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(item.title.isEmpty ? item.releaseTitle : item.title)
                .font(KDFont.section)
                .lineLimit(2)
                .accessibilityAddTraits(.isHeader)
            Spacer(minLength: KD.Space.m)
            Button(tr("Готово", "Done")) { dismiss() }
                .buttonStyle(KDSecondaryButtonStyle())
                .keyboardShortcut(.cancelAction)
        }
    }

    private var searchField: some View {
        HStack(spacing: KD.Space.xs) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary).accessibilityHidden(true)
            TextField(tr("Найти файл", "Find a file"), text: $query)
                .textFieldStyle(.plain)
                .focused($searchFocused)
        }
        .kdFieldChrome(focused: searchFocused)
    }

    @ViewBuilder private var listArea: some View {
        if files.isEmpty {
            placeholder
        } else if filtered.isEmpty {
            Text(tr("Ничего не найдено", "Nothing found"))
                .font(KDFont.secondary)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            List(filtered) { file in fileRow(file) }
                .listStyle(.inset)
        }
    }

    @ViewBuilder private var placeholder: some View {
        if error == nil {
            VStack(spacing: KD.Space.s) {
                ProgressView()
                Text(loaded
                    ? tr("Список файлов пока пуст. Он появится, когда раздача передаст метаданные.", "The file list is empty for now. It appears once the torrent sends its metadata.")
                    : tr("Получаем список файлов…", "Getting the file list…"))
                    .font(KDFont.secondary)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            Spacer(minLength: 0)
        }
    }

    private func fileRow(_ file: DownloadFile) -> some View {
        let name = NSString(string: file.name).lastPathComponent
        let parent = NSString(string: file.name).deletingLastPathComponent
        return HStack(alignment: .center, spacing: KD.Space.s) {
            VStack(alignment: .leading, spacing: 3) {
                Text(name).lineLimit(1).truncationMode(.middle).help(file.name)
                if !parent.isEmpty {
                    Text(parent).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
                }
                ProgressView(value: min(100, max(0, file.progress)), total: 100)
                    .tint(KDPalette.accent)
                    .accessibilityLabel(name)
                    .accessibilityValue(KDFormat.percent(file.progress))
            }
            VStack(alignment: .trailing, spacing: 2) {
                Text(KDFormat.percent(file.progress)).font(KDFont.meta.monospacedDigit())
                Text(KDFormat.bytes(file.size)).font(.caption).foregroundStyle(.secondary)
            }
            .fixedSize()
            KDIconButton(systemImage: "folder", label: tr("Показать в Finder", "Show in Finder")) { reveal(file) }
        }
        .padding(.vertical, 2)
    }

    private func reveal(_ file: DownloadFile) {
        let manager = FileManager.default
        let candidates: [String] = [file.fullPath, file.incompletePath]
        if let path = candidates.first(where: { !$0.isEmpty && manager.fileExists(atPath: $0) }) {
            NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)])
        } else {
            DownloadsActions.openFolder(of: item, model: model)
        }
    }

    private func poll() async {
        while !Task.isCancelled {
            await load()
            try? await Task.sleep(for: .seconds(2))
        }
    }

    private func load() async {
        do {
            let result = try await model.bridge.call("files", ["id": .string(item.id)], as: FilesResult.self)
            files = result.files
            error = nil
            loaded = true
        } catch {
            if Task.isCancelled { return }
            if let text = model.message(for: error) { self.error = text }
        }
    }
}

// MARK: - Diagnostics sheet

private struct TransferDiagnosticFact: Identifiable {
    let id: String
    let label: String
    let value: String
}

struct TransferDiagnosticsView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.dismiss) var dismiss
    var item: Transfer
    @State private var result: JSONValue?
    @State private var error: String?
    @State private var actionError: String?
    @State private var notice: String?
    @State private var working = false
    @State private var confirmLogs = false

    private var diagnosis: JSONValue {
        guard let result else { return JSONValue.null }
        return result["diagnosis"]
    }
    private var snapshot: JSONValue {
        guard let result else { return JSONValue.null }
        return result["snapshot"]
    }
    private var isPaused: Bool { item.paused || downloadsBool(snapshot["paused"]) == true }
    private var canRetry: Bool { diagnosis["canRetryConnections"].bool }

    private var titleColor: Color {
        let level = downloadsNumber(diagnosis["level"]) ?? 1
        if level >= 2 { return KDPalette.danger }
        if level < 1 { return KDPalette.success }
        return Color.primary
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            header
            ScrollView {
                VStack(alignment: .leading, spacing: KD.Space.m) {
                    mainContent
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            footer
        }
        .padding(KD.Space.l)
        .frame(minWidth: 540, idealWidth: 560, minHeight: 440)
        .task { await poll() }
        .confirmationDialog(
            tr("Скопировать логи?", "Copy the logs?"),
            isPresented: $confirmLogs,
            titleVisibility: Visibility.visible
        ) {
            Button(tr("Скопировать", "Copy")) { copyLogs() }
            Button(tr("Отмена", "Cancel"), role: .cancel) {}
        } message: {
            Text(tr("Диагностический отчёт будет скопирован в буфер обмена. Перед отправкой его можно просмотреть.", "The diagnostic report will be copied to the clipboard. You can review it before sending."))
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline) {
                Text(tr("Почему не скачивается?", "Why is it not downloading?"))
                    .font(KDFont.section)
                    .accessibilityAddTraits(.isHeader)
                Spacer(minLength: KD.Space.m)
                Button(tr("Готово", "Done")) { dismiss() }
                    .buttonStyle(KDSecondaryButtonStyle())
                    .keyboardShortcut(.cancelAction)
            }
            Text(item.title.isEmpty ? item.releaseTitle : item.title)
                .font(KDFont.secondary)
                .foregroundStyle(.secondary)
                .lineLimit(2)
        }
    }

    @ViewBuilder private var mainContent: some View {
        if result != nil {
            diagnosisBlock
            actionsRow
            if let notice {
                Label(notice, systemImage: "checkmark.circle")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.success)
            }
            if let actionError {
                KDBanner(message: actionError, tone: KDTone.danger, onDismiss: { self.actionError = nil })
            }
            if let error {
                KDBanner(message: error, tone: KDTone.danger, actionTitle: tr("Повторить", "Retry"), onAction: { Task { await load() } })
            }
            factsBlock
            trackersBlock
        } else if let error {
            KDBanner(message: error, tone: KDTone.danger, actionTitle: tr("Повторить", "Retry"), onAction: { Task { await load() } })
        } else {
            HStack(spacing: KD.Space.s) {
                ProgressView().controlSize(.small)
                Text(tr("Проверяем загрузку…", "Checking the download…")).foregroundStyle(.secondary)
            }
        }
    }

    private var diagnosisBlock: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            Text(tr(diagnosis["title"].string)).font(Font.title3.weight(.semibold)).foregroundStyle(titleColor)
            Text(tr(diagnosis["explanation"].string)).fixedSize(horizontal: false, vertical: true)
            Text(tr(diagnosis["recommendation"].string)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .textSelection(.enabled)
    }

    private var actionsRow: some View {
        KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
            if isPaused {
                Button(tr("Продолжить", "Resume")) { resume() }
                    .buttonStyle(KDPrimaryButtonStyle())
            }
            if canRetry {
                Button(tr("Повторить соединение", "Retry connections")) { retryConnections() }
                    .buttonStyle(KDSecondaryButtonStyle())
            }
            Button(tr("Открыть папку", "Open folder")) { DownloadsActions.openFolder(of: item, model: model) }
                .buttonStyle(KDSecondaryButtonStyle())
            if item.mediaId != nil {
                Button(tr("Выбрать другую раздачу", "Choose another release")) {
                    dismiss()
                    DownloadsActions.openCard(for: item, model: model)
                }
                .buttonStyle(KDSecondaryButtonStyle())
            }
        }
        .disabled(working)
    }

    // MARK: Facts

    private var facts: [TransferDiagnosticFact] {
        let data = snapshot
        var rows: [TransferDiagnosticFact] = []
        if let network = downloadsBool(data["networkAvailable"]) {
            let text = network ? tr("Сетевое подключение есть", "The network is connected") : tr("Нет подключения к сети", "No network connection")
            rows.append(TransferDiagnosticFact(id: "network", label: tr("Сеть", "Network"), value: text))
        } else {
            rows.append(TransferDiagnosticFact(id: "network", label: tr("Сеть", "Network"), value: tr("Состояние пока неизвестно", "Status is not known yet")))
        }
        let metadata = downloadsBool(data["hasMetadata"]) ?? false
        rows.append(TransferDiagnosticFact(id: "files", label: tr("Файлы", "Files"), value: metadata ? tr("Список файлов получен", "The file list is known") : tr("Ждём список файлов", "Waiting for the file list")))
        let connections = Int(downloadsNumber(data["connections"]) ?? 0)
        let seeds = Int(downloadsNumber(data["seeds"]) ?? 0)
        let peerText = tr("Подключено: \(connections) · отдают: \(seeds)", "Connected: \(connections) · seeding: \(seeds)")
        rows.append(TransferDiagnosticFact(id: "peers", label: tr("Участники", "Peers"), value: peerText))
        if let rate = downloadsNumber(data["downloadRate"]) {
            rows.append(TransferDiagnosticFact(id: "rate", label: tr("Скорость", "Speed"), value: KDFormat.rate(rate)))
        }
        let space = data["spaceCheck"]
        let available = downloadsNumber(space["availableBytes"]) ?? downloadsNumber(data["freeBytes"])
        if let available {
            var text = tr("Свободно ", "Free ") + KDFormat.bytes(available)
            if let required = downloadsNumber(space["requiredBytes"]) {
                text += " · " + tr("нужно ", "needed ") + KDFormat.bytes(required)
            }
            rows.append(TransferDiagnosticFact(id: "space", label: tr("Свободное место", "Free space"), value: text))
        }
        return rows
    }

    private var factsBlock: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            ForEach(facts) { fact in
                HStack(alignment: .firstTextBaseline, spacing: KD.Space.s) {
                    Text(fact.label).foregroundStyle(.secondary).frame(width: 130, alignment: .leading)
                    Text(fact.value).fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                }
                .font(KDFont.secondary)
            }
        }
        .padding(KD.Space.s)
        .frame(maxWidth: .infinity, alignment: .leading)
        .kdCard(radius: KD.Radius.control, level: KDCardLevel.sunken)
    }

    // MARK: Trackers

    private var trackers: [JSONValue] { snapshot["trackers"].array }

    private func trackerStatus(_ tracker: JSONValue) -> String {
        if tracker["responded"].bool { return tr("Ответ получен", "Responded") }
        if tracker["failed"].bool { return tr("Нет ответа или ошибка", "No response or an error") }
        return tr("Ответ пока не получен", "No response yet")
    }

    private func trackerColor(_ tracker: JSONValue) -> Color {
        if tracker["responded"].bool { return KDPalette.success }
        if tracker["failed"].bool { return KDPalette.danger }
        return Color.secondary
    }

    @ViewBuilder private var trackersBlock: some View {
        let all = trackers
        if !all.isEmpty {
            let shown = Array(all.prefix(40))
            VStack(alignment: .leading, spacing: KD.Space.xs) {
                Text(tr("Трекеры", "Trackers")).font(KDFont.rowTitle).accessibilityAddTraits(.isHeader)
                ForEach(Array(shown.enumerated()), id: \.offset) { _, tracker in
                    HStack(alignment: .firstTextBaseline, spacing: KD.Space.xs) {
                        Text(tracker["host"].string).lineLimit(1).truncationMode(.middle)
                        Text(tracker["protocol"].string.uppercased()).font(.caption).foregroundStyle(.secondary)
                        Spacer(minLength: KD.Space.xs)
                        Text(trackerStatus(tracker)).foregroundStyle(trackerColor(tracker)).lineLimit(1)
                    }
                    .font(KDFont.secondary)
                }
                if all.count > shown.count {
                    Text(tr("…и ещё \(all.count - shown.count)", "…and \(all.count - shown.count) more"))
                        .font(KDFont.secondary)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private var footer: some View {
        HStack {
            Button(tr("Скопировать логи", "Copy logs")) { confirmLogs = true }
                .buttonStyle(KDSecondaryButtonStyle())
            Spacer(minLength: 0)
        }
    }

    // MARK: Actions

    private func poll() async {
        while !Task.isCancelled {
            await load()
            try? await Task.sleep(for: .seconds(2))
        }
    }

    private func load() async {
        do {
            result = try await model.bridge.call("diagnostics", ["id": .string(item.id)])
            error = nil
        } catch {
            if Task.isCancelled { return }
            if let text = model.message(for: error) { self.error = text }
        }
    }

    private func run(_ method: String, success: String) {
        if working { return }
        working = true
        notice = nil
        actionError = nil
        let previous: String? = model.error
        model.error = nil
        let id = item.id
        Task {
            await model.transferAction(method, id: id, deleteFiles: false)
            let failure: String? = model.error
            model.error = previous
            if let failure {
                actionError = failure
            } else {
                notice = success
            }
            await load()
            working = false
        }
    }

    private func retryConnections() {
        run("retry", success: tr("Соединение повторено. Результат появится через несколько секунд.", "Connections were retried. The result appears in a few seconds."))
    }

    private func resume() {
        run("toggle", success: tr("Загрузка продолжена.", "The download was resumed."))
    }

    private func copyLogs() {
        Task {
            if await model.logs() {
                notice = tr("Логи скопированы в буфер обмена.", "The logs were copied to the clipboard.")
            }
        }
    }
}

// MARK: - Speed limits

private enum SpeedLimitField: Hashable {
    case down, up
}

/// Download and upload limits in KB/s. An empty field means no limit.
/// Used inline on the Settings page and in the popover on the Downloads page.
struct SpeedLimitsView: View {
    @EnvironmentObject var model: AppModel
    var onApplied: (() -> Void)?
    @State private var downText = ""
    @State private var upText = ""
    @State private var errorText: String?
    @State private var saved = false
    @State private var applying = false
    @FocusState private var focus: SpeedLimitField?

    private static let maximum = 2_097_151

    init(onApplied: (() -> Void)? = nil) {
        self.onApplied = onApplied
    }

    private func parse(_ text: String) -> Int? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return 0 }
        guard let value = Int(trimmed), value >= 0, value <= SpeedLimitsView.maximum else { return nil }
        return value
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(tr("Ограничения скорости", "Speed limits")).font(.headline)
            limitRow(tr("Загрузка", "Download"), text: $downText, field: SpeedLimitField.down)
            limitRow(tr("Отдача", "Upload"), text: $upText, field: SpeedLimitField.up)
            hintLine
            HStack(spacing: KD.Space.s) {
                Button(tr("Применить", "Apply")) { commit(close: true) }
                    .buttonStyle(KDPrimaryButtonStyle())
                if saved {
                    Label(tr("Сохранено", "Saved"), systemImage: "checkmark.circle")
                        .font(KDFont.secondary)
                        .foregroundStyle(KDPalette.success)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .task { reload() }
        .onChange(of: model.settings?.maxDownloadKbps) { _, _ in if focus == nil { reload() } }
        .onChange(of: model.settings?.maxUploadKbps) { _, _ in if focus == nil { reload() } }
        .onChange(of: focus) { old, next in
            if old != nil && old != next { commit(close: false) }
        }
        .onDisappear {
            // Typed values are saved at once, also when the page or popover goes away before a field loses focus.
            if !applying { commit(close: false) }
        }
    }

    @ViewBuilder private var hintLine: some View {
        if let errorText {
            Text(errorText).font(.callout).foregroundStyle(KDPalette.danger).fixedSize(horizontal: false, vertical: true)
        } else {
            Text(tr("Пустое поле или 0 — без ограничений.", "An empty field or 0 means no limit.")).font(.callout).foregroundStyle(.secondary)
        }
    }

    private func limitRow(_ title: String, text: Binding<String>, field: SpeedLimitField) -> some View {
        let unit = tr("КБ/с", "KB/s")
        return HStack(spacing: 12) {
            Text(title).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            TextField(title, text: text, prompt: Text(tr("без ограничений", "no limit")))
                .labelsHidden()
                .textFieldStyle(.roundedBorder)
                .multilineTextAlignment(.trailing)
                .font(.body.monospacedDigit())
                .frame(width: 140)
                .focused($focus, equals: field)
                .onSubmit { commit(close: true) }
                .accessibilityLabel(title + ", " + unit)
            Text(unit).foregroundStyle(.secondary).frame(width: 40, alignment: .leading)
        }
    }

    private func display(_ value: Int) -> String { value == 0 ? "" : String(value) }

    private func reload() {
        downText = display(model.settings?.maxDownloadKbps ?? 0)
        upText = display(model.settings?.maxUploadKbps ?? 0)
        errorText = nil
    }

    private func commit(close: Bool) {
        guard let down = parse(downText), let up = parse(upText) else {
            errorText = tr("Введи число от 0 до 2 097 151 или оставь поле пустым.", "Enter a number from 0 to 2,097,151, or leave the field empty.")
            return
        }
        errorText = nil
        let currentDown = model.settings?.maxDownloadKbps ?? 0
        let currentUp = model.settings?.maxUploadKbps ?? 0
        if down == currentDown && up == currentUp {
            if close { onApplied?() }
            return
        }
        applying = true
        Task {
            await model.setMany(["maxDownloadKbps": .number(Double(down)), "maxUploadKbps": .number(Double(up))])
            applying = false
            let stored = model.settings?.maxDownloadKbps == down && model.settings?.maxUploadKbps == up
            if stored {
                saved = true
                if close { onApplied?() }
                try? await Task.sleep(for: .seconds(2))
                saved = false
            }
        }
    }
}
