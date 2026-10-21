import AppKit
import ServiceManagement
import SwiftUI
import UserNotifications

// The "Система" cards: Notifications, Devices on your network, Launch at login.

// MARK: Notifications

struct SettingsNotificationsCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var status: UNAuthorizationStatus?
    @State private var notice: SettingsNotice?
    @State private var testing = false

    private static let systemSettingsAddress = "x-apple.systempreferences:com.apple.Notifications-Settings.extension"

    var body: some View {
        SettingsCard(.notifications) {
            SettingsToggleRow(
                tr("Уведомления о загрузках", "Download notifications"),
                key: "notifyDownloads",
                detail: tr("Одинаковые ошибки не повторяются. Нажатие откроет очередь загрузок.", "Identical errors are not repeated. Clicking a notification opens the download queue."),
                read: { model.settings?.notifyDownloads ?? true },
                afterWrite: { (enabled: Bool) async -> Void in
                    if enabled { _ = await model.enableNotifications() }
                    await refreshStatus()
                }
            )
            if status == UNAuthorizationStatus.denied { deniedBlock }
            Divider()
            testRow
        }
        .task { await refreshStatus() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            Task { await refreshStatus() }
        }
    }

    private var deniedBlock: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            Label(tr("Уведомления для Качалки выключены в настройках macOS.", "Notifications for Ka4alka are turned off in macOS settings."), systemImage: "exclamationmark.triangle.fill")
                .font(KDFont.secondary)
                .foregroundStyle(KDPalette.warning)
                .fixedSize(horizontal: false, vertical: true)
            Button {
                openSystemSettings()
            } label: {
                Label(tr("Открыть настройки уведомлений", "Open notification settings"), systemImage: "gearshape")
            }
            .buttonStyle(KDSecondaryButtonStyle())
        }
    }

    private var testRow: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            HStack(spacing: KD.Space.s) {
                Button {
                    Task { await sendTest() }
                } label: {
                    Label(tr("Проверить уведомление", "Send a test notification"), systemImage: "bell.badge")
                }
                .buttonStyle(KDSecondaryButtonStyle())
                .disabled(testing)
                if testing { SettingsSpinner() }
            }
            SettingsNoticeLine(notice: $notice)
        }
    }

    @MainActor private func refreshStatus() async {
        status = await model.notificationStatus()
    }

    @MainActor private func sendTest() async {
        testing = true
        notice = nil
        defer { testing = false }
        await model.sendTestNotification()
        await refreshStatus()
        let current: UNAuthorizationStatus? = status
        if current == UNAuthorizationStatus.denied {
            notice = SettingsNotice.failure(tr("macOS не разрешает уведомления для Качалки.", "macOS does not allow notifications for Ka4alka."))
        } else if current == UNAuthorizationStatus.authorized || current == UNAuthorizationStatus.provisional {
            notice = SettingsNotice.ok(tr("Уведомление отправлено. Если его не видно, проверь режим «Не беспокоить».", "The notification was sent. If you cannot see it, check Do Not Disturb."))
        }
    }

    private func openSystemSettings() {
        if let url = URL(string: SettingsNotificationsCard.systemSettingsAddress) { NSWorkspace.shared.open(url) }
    }
}

// MARK: Devices on the network

struct SettingsLanCard: View {
    @EnvironmentObject private var model: AppModel
    @State private var discovering = false
    /// The device a pairing request is in flight for; one request at a time.
    @State private var pairingID: String?
    @State private var pairingUntil: Date?
    @State private var nameDraft = ""
    @State private var savedName = ""
    @State private var renaming = false
    @State private var notice: SettingsNotice?

    private static let nameLimit = 64

    private var enabled: Bool { model.settings?.lanEnabled ?? false }
    private var devices: [LanDevice] { model.lanState?.devices ?? [] }
    private var trimmedDraft: String { nameDraft.trimmingCharacters(in: .whitespacesAndNewlines) }
    private var canRename: Bool { !trimmedDraft.isEmpty && trimmedDraft != savedName && !renaming }

    var body: some View {
        SettingsCard(.lan) {
            PhoneRemoteSettings()
            Divider()
            SettingsToggleRow(
                tr("Разрешить связь с другими компьютерами", "Enable local network devices"),
                key: "lanEnabled",
                detail: tr("Сопряжение работает с версиями для Windows и Mac. Подтверди одинаковый код на обоих компьютерах.", "Pair with Windows and Mac versions. Confirm the matching code on both computers."),
                read: { model.settings?.lanEnabled ?? false },
                afterWrite: { (on: Bool) async -> Void in await lanSwitched(on) }
            )
            if enabled { enabledBlock }
        }
        .task {
            savedName = model.lanState?.name ?? ""
            nameDraft = savedName
            if enabled { await discover() }
        }
        .onChange(of: model.lanState?.name) { _, newName in
            guard let newName else { return }
            if nameDraft == savedName || nameDraft.isEmpty { nameDraft = newName }
            savedName = newName
        }
    }

    // MARK: Enabled content

    @ViewBuilder private var enabledBlock: some View {
        Divider()
        nameBlock
        Divider()
        ViewThatFits(in: .horizontal) {
            HStack(spacing: KD.Space.s) { pairingButtons }
            VStack(alignment: .leading, spacing: KD.Space.s) { pairingButtons }
        }
        pairingStatus
        deviceList
    }

    private var nameBlock: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(tr("Имя этого устройства", "Name of this device")).font(KDFont.rowTitle)
            HStack(spacing: KD.Space.s) {
                TextField(tr("Имя устройства", "Device name"), text: $nameDraft)
                    .textFieldStyle(.roundedBorder)
                    .frame(maxWidth: 320)
                    .onSubmit { Task { await rename() } }
                    .onChange(of: nameDraft) { _, value in
                        if value.count > SettingsLanCard.nameLimit { nameDraft = String(value.prefix(SettingsLanCard.nameLimit)) }
                    }
                Button(tr("Сохранить", "Save")) { Task { await rename() } }
                    .buttonStyle(KDSecondaryButtonStyle())
                    .disabled(!canRename)
                if renaming { SettingsSpinner() }
            }
            SettingsCaption(tr("Так это устройство видят другие компьютеры. По умолчанию — название модели.", "This is how other computers see this device. By default it is the model name."))
            SettingsNoticeLine(notice: $notice)
        }
    }

    @ViewBuilder private var pairingButtons: some View {
        HStack(spacing: KD.Space.xs) {
            Button(tr("Найти устройства", "Find devices")) { Task { await discover() } }
                .buttonStyle(KDSecondaryButtonStyle())
                .disabled(discovering)
            if discovering { SettingsSpinner() }
        }
        Button(tr("Разрешить сопряжение на 2 минуты", "Allow pairing for 2 minutes")) { Task { await allowPairing() } }
            .buttonStyle(KDSecondaryButtonStyle())
    }

    @ViewBuilder private var pairingStatus: some View {
        if let until = pairingUntil {
            TimelineView(.periodic(from: Date(), by: 1)) { context in
                let remaining: TimeInterval = until.timeIntervalSince(context.date)
                if remaining > 0 {
                    Label(tr("Сопряжение открыто: ", "Pairing is open: ") + SettingsLanCard.countdown(remaining), systemImage: "timer")
                        .font(KDFont.secondary)
                        .foregroundStyle(KDPalette.accent)
                        .monospacedDigit()
                }
            }
        }
    }

    @ViewBuilder private var deviceList: some View {
        if devices.isEmpty {
            if discovering {
                SettingsCaption(tr("Ищем устройства…", "Looking for devices…"))
            } else {
                SettingsCaption(tr("Устройств не найдено", "No devices found"))
            }
        } else {
            ForEach(devices) { (device: LanDevice) in
                deviceRow(device)
            }
        }
    }

    private func deviceRow(_ device: LanDevice) -> some View {
        HStack(spacing: KD.Space.m) {
            VStack(alignment: .leading, spacing: 4) {
                Text(device.name)
                SettingsCaption(device.address)
            }
            Spacer(minLength: 0)
            if device.paired {
                Label(tr("Подключено", "Paired"), systemImage: "checkmark.shield")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.success)
            } else {
                if pairingID == device.id { SettingsSpinner() }
                Button(tr("Соединить", "Pair")) { Task { await pair(device) } }
                    .buttonStyle(KDSecondaryButtonStyle())
                    .controlSize(.small)
                    .disabled(pairingID != nil)
                    .accessibilityLabel(tr("Соединить с ", "Pair with ") + device.name)
            }
        }
    }

    // MARK: Actions

    @MainActor private func lanSwitched(_ on: Bool) async {
        if on {
            await discover()
        } else {
            model.lanState = nil
            pairingUntil = nil
        }
    }

    @MainActor private func discover() async {
        guard !discovering else { return }
        discovering = true
        defer { discovering = false }
        await model.lan("discover")
    }

    @MainActor private func allowPairing() async {
        if await model.lan("allow") {
            pairingUntil = Date().addingTimeInterval(120)
        }
    }

    @MainActor private func pair(_ device: LanDevice) async {
        guard pairingID == nil else { return }
        pairingID = device.id
        defer { pairingID = nil }
        await model.lan("pair", ["id": .string(device.id)])
    }

    @MainActor private func rename() async {
        guard canRename else { return }
        let value: String = trimmedDraft
        renaming = true
        notice = nil
        defer { renaming = false }
        await model.lan("rename", ["name": .string(value)])
        if model.lanState?.name == value {
            savedName = value
            nameDraft = value
            notice = SettingsNotice.ok(tr("Имя сохранено.", "Name saved."))
        }
    }

    private static func countdown(_ remaining: TimeInterval) -> String {
        let total: Int = max(0, Int(remaining.rounded(.up)))
        let seconds: Int = total % 60
        return String(total / 60) + ":" + (seconds < 10 ? "0" : "") + String(seconds)
    }
}

// MARK: Launch at login

struct SettingsStartupCard: View {
    @State private var status: SMAppService.Status = .notRegistered
    @State private var failure: String?
    @Environment(\.locale) private var locale

    private var isOn: Bool { status == .enabled || status == .requiresApproval }

    private var switchBinding: Binding<Bool> {
        Binding<Bool>(
            get: { isOn },
            set: { (next: Bool) in
                status = next ? SMAppService.Status.enabled : SMAppService.Status.notRegistered
                Task { @MainActor in await change(next) }
            }
        )
    }

    var body: some View {
        let _ = locale
        SettingsCard(.startup) {
            SettingsSwitch(
                tr("Открывать при входе в систему", "Open at login"),
                detail: tr("Качалка запустится вместе с macOS и продолжит загрузки.", "Ka4alka starts with macOS and resumes your downloads."),
                isOn: switchBinding
            )
            if status == .requiresApproval { approvalBlock }
            if let failure {
                Label(failure, systemImage: "exclamationmark.triangle.fill")
                    .font(KDFont.secondary)
                    .foregroundStyle(KDPalette.warning)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        }
        .task { status = SMAppService.mainApp.status }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            status = SMAppService.mainApp.status
        }
    }

    private var approvalBlock: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            SettingsCaption(tr("macOS ждёт подтверждения: разреши Качалку в разделе «Объекты входа».", "macOS is waiting for your approval: allow Ka4alka under Login Items."))
            Button {
                SMAppService.openSystemSettingsLoginItems()
            } label: {
                Label(tr("Открыть «Объекты входа»", "Open Login Items"), systemImage: "gearshape")
            }
            .buttonStyle(KDSecondaryButtonStyle())
        }
    }

    @MainActor private func change(_ next: Bool) async {
        failure = nil
        do {
            if next { try SMAppService.mainApp.register() } else { try await SMAppService.mainApp.unregister() }
        } catch {
            failure = tr("Не удалось изменить автозапуск: ", "Could not change launch at login: ") + error.localizedDescription
        }
        status = SMAppService.mainApp.status
    }
}
