import AppKit
import SwiftUI

@main struct Ka4alkaApp: App {
    init() {
        if let index = CommandLine.arguments.firstIndex(of: "--apply-update"), CommandLine.arguments.count > index + 1 { exit(MacUpdateInstaller.run(CommandLine.arguments[index + 1])) }
    }
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model = AppModel()
    @StateObject private var updater = MacUpdater()
    var body: some Scene {
        WindowGroup("Качалка") {
            RootView().environmentObject(model).environmentObject(updater)
                .frame(minWidth: 760, minHeight: 520)
                .preferredColorScheme(model.settings?.light == true ? .light : .dark)
                .task {
                    delegate.model = model; delegate.updater = updater; updater.engine = model.bridge
                    model.onUpdateProgress = { [weak updater] value in updater?.progress(value) }
                    await model.bootstrap()
                    let environment = ProcessInfo.processInfo.environment
                    if model.settings != nil, let path = environment["KACHALKA_UPDATE_HEALTH_PATH"], let token = environment["KACHALKA_UPDATE_HEALTH_TOKEN"] {
                        try? JSONEncoder().encode(["token": token, "version": model.version]).write(to: URL(fileURLWithPath: path), options: .atomic)
                    }
                    if ProcessInfo.processInfo.arguments.contains("--smoke-test") { await MacSmoke.run(model: model) }
                    else if model.settings?.checkForUpdates == true { await updater.check(autoDownload: model.settings?.autoUpdate == true) }
                }
                .onOpenURL { url in Task { await model.openURL(url) } }
        }
        .defaultSize(width: 1280, height: 820)
        .windowToolbarStyle(.unified)
        .commands {
            CommandGroup(after: .textEditing) { Button(tr("Поиск", "Search")) { NotificationCenter.default.post(name: Notification.Name("Ka4alkaFocusSearch"), object: nil) }.keyboardShortcut("f") }
            CommandGroup(after: .newItem) { Button(tr("Добавить торрент", "Add torrent")) { model.addTorrent = true }.keyboardShortcut("o") }
            CommandMenu(tr("Медиатека", "Library")) {
                ForEach(Section.allCases) { section in Button(section.title) { model.select(section) } }
            }
        }
    }
}
@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var model: AppModel?
    weak var updater: MacUpdater?
    var quitting = false
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard !quitting, let model else { return .terminateNow }; quitting = true
        Task {
            await model.bridge.shutdown()
            if let updater, updater.ready, !updater.installing, model.settings?.autoUpdate == true {
                do { try updater.install(terminate: false) }
                catch { model.error = error.localizedDescription; quitting = false; sender.reply(toApplicationShouldTerminate: false); return }
            }
            sender.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }
}
