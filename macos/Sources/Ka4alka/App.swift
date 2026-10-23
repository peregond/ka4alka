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
        // A single window: opening a magnet link, a .torrent file or a kachalka:// link
        // reuses it instead of creating a second window with mirrored sheets.
        Window(tr("Качалка", "Ka4alka"), id: "main") { mainContent }
            .defaultSize(width: 1280, height: 820)
            .windowToolbarStyle(.unified)
            .commands { ShellCommands(model: model, updater: updater, canGoBack: model.routes.count > 1, refreshing: model.refreshing, language: model.settings?.uiLanguage ?? "ru") }
    }
    private var interfaceLocale: Locale { Locale(identifier: model.settings?.uiLanguage == "en" ? "en" : "ru") }
    private var mainContent: some View {
        RootView()
            .environmentObject(model)
            .environmentObject(updater)
            .frame(minWidth: KD.Width.minWindow, minHeight: KD.Width.minWindowHeight)
            .environment(\.locale, interfaceLocale)
            .environment(\.kdLite, model.settings?.liteMode == true)
            .preferredColorScheme(model.appearance.colorScheme)
            .task { await startUp() }
            .onChange(of: model.appearance) { _, mode in NSApp.appearance = mode.nsAppearance }
            .onOpenURL { url in Task { await model.openURL(url) } }
    }
    /// Runs once per launch, whichever way the window content is rebuilt.
    @MainActor private func startUp() async {
        guard !delegate.didStart else { return }
        delegate.didStart = true
        delegate.model = model; delegate.updater = updater; updater.engine = model.bridge; ImageStore.shared.engine = model.bridge
        model.onUpdateProgress = { [weak updater] value in updater?.progress(value) }
        await model.bootstrap()
        // A cancelled start-up (the window content was rebuilt) leaves neither a connection nor an
        // error to show, so let the next task run start it again.
        if Task.isCancelled, !model.connected { delegate.didStart = false; return }
        if let failure = updater.consumeInstallFailure() { model.toast = failure }
        let environment = ProcessInfo.processInfo.environment
        if model.settings != nil, let path = environment["KACHALKA_UPDATE_HEALTH_PATH"], let token = environment["KACHALKA_UPDATE_HEALTH_TOKEN"] {
            try? JSONEncoder().encode(["token": token, "version": model.version]).write(to: URL(fileURLWithPath: path), options: .atomic)
        }
        // The update helper starts the new version as a plain child process, which the system leaves in the background:
        // its window opened behind everything. Bring it forward once it is up.
        if environment["KACHALKA_UPDATE_HEALTH_PATH"] != nil { delegate.bringToFront() }
        if ProcessInfo.processInfo.arguments.contains("--close-smoke-test") { await MacSmoke.close(model: model) }
        else if ProcessInfo.processInfo.arguments.contains("--smoke-test") { await MacSmoke.run(model: model) }
        else { await updater.restore(); if model.settings?.checkForUpdates == true { await updater.check(autoDownload: model.settings?.autoUpdate == true) } }
    }
}

/// Menu bar commands. The flags and the language are plain values so the menu items are rebuilt when they change.
struct ShellCommands: Commands {
    let model: AppModel
    let updater: MacUpdater
    var canGoBack: Bool
    var refreshing: Bool
    var language: String
    private static let releaseNotes = URL(string: "https://github.com/peregond/ka4alka/releases/latest")
    /// Library section number n opens with Command-n.
    private static func key(for section: Section) -> KeyEquivalent {
        let number = (Section.allCases.firstIndex(of: section) ?? 0) + 1
        return KeyEquivalent(Character(String(number)))
    }
    var body: some Commands {
        // Software update belongs in the app menu, right under About. The result is shown as a toast: a check that
        // finds nothing would otherwise look like a command that did nothing.
        CommandGroup(after: .appInfo) {
            Button(tr("Проверить обновления…", "Check for Updates…")) {
                Task { @MainActor in
                    await updater.check(autoDownload: model.settings?.autoUpdate == true)
                    if !updater.statusText.isEmpty { model.toast = updater.statusText }
                }
            }
        }
        CommandGroup(replacing: .appSettings) {
            Button(tr("Настройки…", "Settings…")) { Task { @MainActor in model.select(.settings) } }.keyboardShortcut(",")
        }
        CommandGroup(after: .toolbar) {
            Button(model.sidebarVisibility == .detailOnly ? tr("Показать боковое меню", "Show Sidebar") : tr("Скрыть боковое меню", "Hide Sidebar")) {
                model.toggleSidebar()
            }.keyboardShortcut("s", modifiers: [.control, .command])
        }
        CommandGroup(replacing: .newItem) {
            Button(tr("Добавить торрент…", "Add Torrent…")) { Task { @MainActor in model.addTorrent = true } }.keyboardShortcut("o")
        }
        CommandMenu(tr("Медиатека", "Library")) {
            Button(tr("Поиск", "Search")) { Task { @MainActor in model.openSearch() } }.keyboardShortcut("f")
            Divider()
            ForEach(Section.allCases) { section in
                Button(section.title) { Task { @MainActor in model.select(section) } }.keyboardShortcut(ShellCommands.key(for: section), modifiers: .command)
            }
            Divider()
            Button(tr("Назад", "Back")) { Task { @MainActor in model.back() } }.keyboardShortcut("[").disabled(!canGoBack)
            Button(tr("Обновить каталог", "Refresh Catalog")) { Task { await model.refresh() } }.keyboardShortcut("r").disabled(refreshing)
        }
        CommandGroup(replacing: .help) {
            Button(tr("Что нового в Качалке", "What’s New in Ka4alka")) {
                if let url = ShellCommands.releaseNotes { NSWorkspace.shared.open(url) }
            }
        }
    }
}
@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var model: AppModel?
    weak var updater: MacUpdater?
    var quitting = false
    var cleanupComplete = false
    /// Set when the start-up work has begun, so a rebuilt window never repeats it.
    var didStart = false
    func applicationDidFinishLaunching(_ notification: Notification) {
        // The stored choice is read synchronously, so the first frame already has the right theme.
        NSApp.appearance = AppearanceMode.stored.nsAppearance
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
    }
    /// Puts the main window in front of other apps. `NSApp.activate()` alone is ignored for a process the system did not
    /// launch itself, so LaunchServices is also asked to activate the application.
    func bringToFront() {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        for window in NSApp.windows where window.canBecomeMain {
            if window.isMiniaturized { window.deminiaturize(nil) }
            window.makeKeyAndOrderFront(nil)
            window.orderFrontRegardless()
        }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.openApplication(at: Bundle.main.bundleURL, configuration: configuration, completionHandler: nil)
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
