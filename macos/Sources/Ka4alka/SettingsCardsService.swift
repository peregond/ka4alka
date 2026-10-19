import AppKit
import SwiftUI

// The "Обслуживание" cards: Updates, News and support, Cache, Help and diagnostics.

// MARK: Updates

struct SettingsUpdatesCard: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var updater: MacUpdater

    private var shownStatus: String {
        updater.statusText.isEmpty ? tr("Обновления ещё не проверялись", "Updates have not been checked yet") : updater.statusText
    }
    private var statusIcon: String {
        if updater.isError { return "exclamationmark.triangle.fill" }
        return updater.ready ? "checkmark.circle.fill" : "info.circle"
    }
    private var statusTone: KDTone {
        if updater.isError { return KDTone.warning }
        return updater.ready ? KDTone.accent : KDTone.neutral
    }

    var body: some View {
        SettingsCard(.updates) {
            HStack {
                Text(tr("Версия приложения", "App version"))
                Spacer(minLength: KD.Space.m)
                Text(updater.installedVersion).monospacedDigit().foregroundStyle(Color.secondary)
            }
            Divider()
            SettingsToggleRow(
                tr("Проверять обновления при запуске", "Check for updates on launch"),
                key: "checkForUpdates",
                read: { model.settings?.checkForUpdates ?? true }
            )
            Divider()
            SettingsToggleRow(
                tr("Скачивать и устанавливать при выходе", "Download and install on quit"),
                key: "autoUpdate",
                detail: tr("Перед установкой сохраняем очередь. Скачиваются только изменившиеся части; настройки и файлы не затрагиваются.", "The queue is saved before installing. Only the parts that changed are downloaded; settings and files are untouched."),
                read: { model.settings?.autoUpdate ?? true }
            )
            Divider()
            statusRow
            progressRow
            actions
        }
    }

    private var statusRow: some View {
        HStack(alignment: .top, spacing: KD.Space.s) {
            Image(systemName: statusIcon)
                .foregroundStyle(statusTone.foreground)
                .padding(.top, 2)
                .accessibilityHidden(true)
            Text(shownStatus)
                .font(KDFont.secondary)
                .foregroundStyle(updater.isError ? KDPalette.warning : Color.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
            Spacer(minLength: 0)
            if updater.busy && updater.progressValue == nil { SettingsSpinner() }
        }
    }

    @ViewBuilder private var progressRow: some View {
        if let value = updater.progressValue {
            ProgressView(value: value)
                .tint(KDPalette.accent)
                .accessibilityLabel(tr("Загрузка обновления", "Downloading the update"))
                .accessibilityValue(KDFormat.percent(value * 100))
        }
    }

    private var actions: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: KD.Space.m) { actionItems }
            VStack(alignment: .leading, spacing: KD.Space.s) { actionItems }
        }
    }

    @ViewBuilder private var actionItems: some View {
        Button(updater.buttonTitle) { Task { await updater.primaryAction() } }
            .buttonStyle(KDPrimaryButtonStyle())
            .controlSize(.large)
            .disabled(updater.busy)
        Link(destination: URL(string: "https://github.com/peregond/ka4alka/releases/latest")!) {
            Text(tr("Что нового", "Release notes")).foregroundStyle(KDPalette.accent)
        }
    }
}

// MARK: News and support

struct SettingsCommunityCard: View {
    @State private var notice: SettingsNotice?

    var body: some View {
        SettingsCard(.community) {
            ViewThatFits(in: .horizontal) {
                HStack(spacing: KD.Space.s) { buttons }
                VStack(alignment: .leading, spacing: KD.Space.s) { buttons }
            }
            SettingsNoticeLine(notice: $notice)
        }
    }

    @ViewBuilder private var buttons: some View {
        Button {
            open("https://t.me/ka4alkacc")
        } label: {
            Label(tr("Новости в Telegram", "News on Telegram"), systemImage: "bell")
        }
        .buttonStyle(KDSecondaryButtonStyle())
        .help("https://t.me/ka4alkacc")
        .accessibilityAddTraits(.isLink)
        Button {
            open("https://t.me/+6sT2ocaKvLxlNmRi")
        } label: {
            Label(tr("Сообщить об ошибке", "Report a bug"), systemImage: "bubble.left.and.bubble.right")
        }
        .buttonStyle(KDSecondaryButtonStyle())
        .help(tr("Открыть чат поддержки в Telegram", "Open the support chat on Telegram"))
        .accessibilityAddTraits(.isLink)
    }

    private func open(_ address: String) {
        if let url = URL(string: address), NSWorkspace.shared.open(url) { return }
        notice = SettingsNotice.failure(tr("Не удалось открыть Telegram. Попробуй открыть ссылку в браузере.", "Telegram could not be opened. Try opening the link in a browser."))
    }
}

// MARK: Cache

struct SettingsCacheCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var cacheBytes: Double?
    @State private var usageFailed = false
    @State private var clearing = false
    @State private var notice: SettingsNotice?

    var body: some View {
        SettingsCard(.cache) {
            usageRow
            HStack(spacing: KD.Space.s) {
                Button {
                    Task { await clearCache() }
                } label: {
                    Label(tr("Очистить кэш", "Clear cache"), systemImage: "trash")
                }
                .buttonStyle(KDSecondaryButtonStyle())
                .disabled(clearing)
                if clearing { SettingsSpinner() }
            }
            SettingsNoticeLine(notice: $notice)
            SettingsCaption(tr("Скачанные файлы, очередь, «Сохранённое» и настройки не затрагиваются.", "Downloaded files, the queue, Saved titles and settings are not affected."))
        }
        .task { await loadUsage() }
    }

    @ViewBuilder private var usageRow: some View {
        HStack(spacing: KD.Space.s) {
            Text(tr("Занято на диске", "Disk usage"))
            Spacer(minLength: KD.Space.m)
            if let bytes = cacheBytes {
                let size: String = KDFormat.bytes(bytes)
                Text(tr("\(size) из 1 ГБ", "\(size) of 1 GB")).monospacedDigit().foregroundStyle(Color.secondary)
            } else if usageFailed {
                Label(tr("Не удалось посчитать", "Could not calculate"), systemImage: "exclamationmark.triangle.fill")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.warning)
            } else {
                SettingsSpinner()
                Text(tr("Считаем…", "Calculating…")).foregroundStyle(Color.secondary)
            }
        }
    }

    @MainActor private func loadUsage() async {
        do {
            let result = try await model.bridge.call("cacheUsage")
            cacheBytes = result["bytes"].number
            usageFailed = false
        } catch {
            if model.message(for: error) != nil { usageFailed = true }
        }
    }

    @MainActor private func clearCache() async {
        let before: Double? = cacheBytes
        clearing = true
        notice = nil
        defer { clearing = false }
        do {
            let result = try await model.bridge.call("clearCache")
            let remaining: Double = result["bytes"].number
            cacheBytes = remaining
            usageFailed = false
            ImageStore.shared.clear()
            var freed: Double = result["freedBytes"].number
            if freed <= 0, let before { freed = max(0, before - remaining) }
            let tail: String = result["failed"].number > 0
                ? tr(". Некоторые файлы заняты — очистка повторится автоматически.", ". Some files are in use; cleanup will repeat automatically.")
                : "."
            notice = SettingsNotice.ok(tr("Освобождено ", "Freed ") + KDFormat.bytes(freed) + tail)
        } catch {
            notice = SettingsNotice.failure(model.message(for: error) ?? error.localizedDescription)
        }
    }
}

// MARK: Help and diagnostics

struct SettingsHelpCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var notice: SettingsNotice?

    var body: some View {
        SettingsCard(.help) {
            Button {
                Task { await copyLogs() }
            } label: {
                Label(tr("Скопировать логи", "Copy logs"), systemImage: "doc.on.doc")
            }
            .buttonStyle(KDSecondaryButtonStyle())
            SettingsNoticeLine(notice: $notice)
            SettingsCaption(tr("Скопируй отчёт и приложи его к сообщению в чате поддержки.", "Copy the report and attach it to your message in the support chat."))
        }
    }

    @MainActor private func copyLogs() async {
        notice = nil
        await model.logs()
        if model.toast == tr("Логи скопированы", "Logs copied") {
            notice = SettingsNotice.ok(tr("Диагностический отчёт скопирован.", "The diagnostic report was copied."))
        }
    }
}
