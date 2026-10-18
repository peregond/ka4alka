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
                    delegate.model = model; await model.bootstrap()
                    if ProcessInfo.processInfo.arguments.contains("--smoke-test") { await MacSmoke.run(model: model) }
                    else if model.settings?.checkForUpdates == true { await updater.check(autoDownload: model.settings?.autoUpdate == true) }
                }
                .onOpenURL { url in model.magnet = url.isFileURL ? url.path : url.absoluteString; model.addTorrent = true }
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
    var quitting = false
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard !quitting, let model else { return .terminateNow }; quitting = true
        Task { await model.bridge.shutdown(); sender.reply(toApplicationShouldTerminate: true) }
        return .terminateLater
    }
}
