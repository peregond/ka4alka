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
                .environment(\.locale, Locale(identifier: model.settings?.uiLanguage == "en" ? "en" : "ru"))
                .preferredColorScheme(model.settings?.light == true ? .light : .dark)
                .task {
                    delegate.model = model; delegate.updater = updater; updater.engine = model.bridge; ImageStore.shared.engine = model.bridge
                    model.onUpdateProgress = { [weak updater] value in updater?.progress(value) }
                    await model.bootstrap()
                    if let updateError = UserDefaults.standard.string(forKey: "Ka4alkaUpdateError") {
                        UserDefaults.standard.removeObject(forKey: "Ka4alkaUpdateError")
                        model.error = updateError
                    }
                    let environment = ProcessInfo.processInfo.environment
                    if model.settings != nil, let path = environment["KACHALKA_UPDATE_HEALTH_PATH"], let token = environment["KACHALKA_UPDATE_HEALTH_TOKEN"] {
                        try? JSONEncoder().encode(["token": token, "version": model.version]).write(to: URL(fileURLWithPath: path), options: .atomic)
                    }
                    if ProcessInfo.processInfo.arguments.contains("--close-smoke-test") { await MacSmoke.close(model: model) }
                    else if ProcessInfo.processInfo.arguments.contains("--smoke-test") { await MacSmoke.run(model: model) }
                    else { await updater.restore(); if model.settings?.checkForUpdates == true { await updater.check(autoDownload: model.settings?.autoUpdate == true) } }
                }
                .onChange(of: model.settings?.light) { _, light in NSApp.appearance = NSAppearance(named: light == true ? .aqua : .darkAqua) }
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
    var cleanupComplete = false
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.appearance = NSAppearance(named: .darkAqua)
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if cleanupComplete { return .terminateNow }
        guard !quitting, let model else { return model == nil ? .terminateNow : .terminateCancel }
        quitting = true
        Task {
            await model.bridge.shutdown()
            if let updater, updater.ready, !updater.installing, model.settings?.autoUpdate == true {
                do { try updater.install(terminate: false) }
                catch {
                    // The engine has already saved and stopped. Complete quit
                    // even if update preparation fails; show the reason on the
                    // next launch instead of leaving an unresponsive window.
                    UserDefaults.standard.set(error.localizedDescription, forKey: "Ka4alkaUpdateError")
                }
            }
            cleanupComplete = true
            requestApplicationQuit()
        }
        // Do not enter AppKit's nested terminateLater event loop while Swift's
        // main executor is still running the caller. Finish cleanup first, then
        // issue a fresh native quit that can return terminateNow immediately.
        return .terminateCancel
    }
}

// AppKit may enter a nested event loop for terminateLater. Calling terminate
// from an active Swift main-actor job would prevent its async close job from
// running in that loop. Leave the Swift job before asking AppKit to quit.
@MainActor func requestApplicationQuit() {
    DispatchQueue.main.async { NSApp.terminate(nil) }
}
