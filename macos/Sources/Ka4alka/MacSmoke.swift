import AppKit
import Foundation

/// Marker shared with the notification observers of the close test.
final class SmokeCloseMarker: @unchecked Sendable { var windowClosed = false }

@MainActor enum MacSmoke {
    /// The one real application window: visible, titled, not a panel and not a sheet.
    static func mainWindow() -> NSWindow? {
        return NSApp.windows.first { (window: NSWindow) -> Bool in
            window.isVisible && window.styleMask.contains(.titled) && !(window is NSPanel) && window.sheetParent == nil && window.contentView != nil
        }
    }

    /// Closes the window the way a person does and records what really happened. The success marker is written
    /// from the real termination path, after the window closed and after the engine was told to stop.
    static func close(model: AppModel) async {
        guard let index = CommandLine.arguments.firstIndex(of: "--close-smoke-test"), CommandLine.arguments.count > index + 1 else { return }
        let directory = URL(fileURLWithPath: CommandLine.arguments[index + 1])
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            try await Task.sleep(for: .milliseconds(300))
            guard model.connected, let window = mainWindow() else { throw UpdateError("Native close test has no connected window") }
            let marker = SmokeCloseMarker()
            let center = NotificationCenter.default
            _ = center.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: nil) { (_: Notification) in
                marker.windowClosed = true
            }
            // applicationShouldTerminate only answers terminateNow after the engine shutdown finished,
            // so this notification is the proof that the whole chain ran.
            _ = center.addObserver(forName: NSApplication.willTerminateNotification, object: nil, queue: nil) { (_: Notification) in
                let report: [String: Bool] = ["connected": true, "windowClosedBeforeTerminate": marker.windowClosed, "terminated": true]
                if let data = try? JSONEncoder().encode(report) { try? data.write(to: directory.appendingPathComponent("checks.json")) }
            }
            window.performClose(nil)
        } catch {
            try? error.localizedDescription.write(to: directory.appendingPathComponent("error.txt"), atomically: true, encoding: .utf8)
            requestApplicationQuit()
        }
    }

    static func run(model: AppModel) async {
        guard let index = CommandLine.arguments.firstIndex(of: "--smoke-test"), CommandLine.arguments.count > index + 1 else { return }
        let root = URL(fileURLWithPath: CommandLine.arguments[index + 1])
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let session = SmokeSession(model: model, root: root)
        await session.execute()
        requestApplicationQuit()
    }
}

/// One screenshot with the facts the pixel audit needs.
struct SmokeShot {
    var bitmap: NSBitmapImageRep
    var scale: CGFloat
    var bounds: CGRect
    var isSheet: Bool
}

@MainActor final class SmokeSession {
    private let model: AppModel
    private let root: URL
    private var checks: [String] = []
    private var failures: [String] = []
    private var notes: [String] = []
    private var warnings: [String] = []
    private var violations: [SmokeViolation] = []
    private var cells: [SmokeCell] = []
    private var metrics: [String: Double] = [:]
    private var environment: [String: String] = [:]
    private var language = "ru"
    private var theme = "dark"
    private var pageName = "bootstrap"
    private var requestedText = ""
    private var currentSize: CGSize = .zero
    private var cards: [String: MediaCard] = [:]
    private var personCandidate: Person?
    private var savedByTest: MediaCard?
    private var lastDigest: [Int: (page: String, signature: UInt64)] = [:]

    private let fullSizes: [CGSize] = [CGSize(width: 760, height: 520), CGSize(width: 1000, height: 700), CGSize(width: 1280, height: 820), CGSize(width: 1920, height: 1080)]
    private let reducedSizes: [CGSize] = [CGSize(width: 760, height: 520), CGSize(width: 1280, height: 820), CGSize(width: 1920, height: 1080)]
    private let primarySize = CGSize(width: 1280, height: 820)

    init(model: AppModel, root: URL) {
        self.model = model
        self.root = root
    }

    // MARK: Recording

    private func stage(_ name: String) {
        try? name.write(to: root.appendingPathComponent("stage.txt"), atomically: true, encoding: .utf8)
    }
    private func sizeText(_ size: CGSize) -> String { return String(Int(size.width.rounded())) + "x" + String(Int(size.height.rounded())) }
    private var context: String {
        let base: String = language + "/" + theme
        return currentSize == .zero ? base : base + " " + sizeText(currentSize)
    }
    /// A named assertion. A failure is recorded and the run continues, so one CI run reports every problem.
    private func check(_ value: Bool, _ text: String) {
        let name: String = "[" + context + "] " + text
        if value { checks.append(name) } else { failures.append("FAILED " + name) }
    }
    /// A named assertion without a context prefix (global facts).
    private func checkGlobal(_ value: Bool, _ text: String) {
        if value { checks.append(text) } else { failures.append("FAILED " + text) }
    }
    private func require(_ value: Bool, _ text: String) throws {
        checkGlobal(value, text)
        if !value { throw UpdateError(text) }
    }
    private func note(_ text: String) { notes.append("[" + context + "] " + text) }
    private func violate(_ category: String, _ text: String) {
        violations.append(SmokeViolation(category: category, page: pageName, width: Int(currentSize.width.rounded()), height: Int(currentSize.height.rounded()), theme: theme, language: language, text: text))
    }
    private func pause(_ milliseconds: Int) async {
        try? await Task.sleep(for: .milliseconds(milliseconds))
    }
    private func window() throws -> NSWindow {
        guard let value = MacSmoke.mainWindow() else { throw UpdateError("The native application window is missing") }
        return value
    }

    /// Polls every 50 ms. Returns whether the condition became true; callers decide whether that is a failure.
    private func waitUntil(_ name: String, timeout: Double, _ condition: () -> Bool) async -> Bool {
        let deadline: Date = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if condition() { return true }
            await pause(50)
        }
        if condition() { return true }
        notes.append("[" + context + "] timeout while waiting for " + name)
        return false
    }

    private func layoutSignature() -> String {
        var parts: [String] = [String(model.revision), String(ImageStore.shared.loadedCount), String(ImageStore.shared.failedCount), String(model.list?.items.count ?? -1), String(model.curated.count)]
        let frames: [String: CGRect] = SettingsLayoutMeasurements.frames
        for id in frames.keys.sorted() {
            if let frame = frames[id] {
                let x: Int = Int(frame.minX)
                let y: Int = Int(frame.minY)
                let w: Int = Int(frame.width)
                let h: Int = Int(frame.height)
                parts.append("\(id):\(x),\(y),\(w),\(h)")
            }
        }
        return parts.joined(separator: ";")
    }

    /// Waits until nothing is loading and the layout (probe frames, counters, revision) is identical on two polls in a row.
    private func settle(_ name: String, timeout: Double = 6) async {
        let deadline: Date = Date().addingTimeInterval(timeout)
        var previous: String = ""
        var stable: Int = 0
        while Date() < deadline {
            let current: String = layoutSignature()
            let idle: Bool = !model.loading && !model.bootstrapping && !model.refreshing
            if idle && current == previous { stable += 1 } else { stable = 0 }
            previous = current
            if stable >= 2 { return }
            await pause(50)
        }
        note("unsettled: " + name)
    }

    // MARK: Run

    func execute() async {
        var fatal: String?
        do {
            try await bootstrapChecks()
            try await runCombination(language: "ru", mode: AppearanceMode.dark, sizes: fullSizes)
            try await runCombination(language: "en", mode: AppearanceMode.light, sizes: reducedSizes)
            await restoreDefaults()
            finalChecks()
        } catch {
            fatal = "FATAL " + error.localizedDescription
            await restoreDefaults()
        }
        stage("shutdown")
        await writeReports(fatal: fatal)
    }

    private func bootstrapChecks() async throws {
        stage("bootstrap")
        currentSize = .zero
        try require(model.connected, "Native SwiftUI application launches its packaged shared engine")
        if let started = smokeProcessStart() {
            let elapsed: Double = Date().timeIntervalSince(started) * 1000
            metrics["connectedByMs"] = elapsed
            checkGlobal(elapsed < 20_000, "Engine is connected within 20 s of the process start")
        } else {
            notes.append("not-instrumented:process-start-time")
        }
        model.folderRequired = false
        let main = try window()
        environment["macOS"] = ProcessInfo.processInfo.operatingSystemVersionString
        environment["surfaceVariant"] = smokeSurfaceVariant()
        environment["glassBranchCompiled"] = smokeGlassBranchCompiled() ? "yes" : "no"
        environment["appVersion"] = model.version
        environment["backingScale"] = String(Double(main.backingScaleFactor))
        if let screen = NSScreen.main { environment["screen"] = sizeText(screen.visibleFrame.size) }
        environment["keyboardUIMode"] = String(UserDefaults.standard.integer(forKey: "AppleKeyboardUIMode"))
        environment["reduceTransparency"] = NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency ? "yes" : "no"
        environment["reduceMotion"] = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? "yes" : "no"
        environment["increaseContrast"] = NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast ? "yes" : "no"
        checkGlobal(!environment["surfaceVariant", default: ""].isEmpty, "The active surface variant is recorded: " + environment["surfaceVariant", default: "?"])
    }

    private func runCombination(language next: String, mode: AppearanceMode, sizes: [CGSize]) async throws {
        language = next
        theme = mode == AppearanceMode.dark ? "dark" : "light"
        lastDigest = [:]
        currentSize = .zero
        stage("combination " + language + "/" + theme)
        await model.set("uiLanguage", .string(next))
        model.setAppearance(mode)
        await pause(400)
        for page in SmokePage.allCases {
            try await runPage(page, sizes: sizes)
        }
    }

    private func runPage(_ page: SmokePage, sizes: [CGSize]) async throws {
        pageName = page.rawValue
        currentSize = .zero
        stage(language + "/" + theme + " " + page.rawValue)
        let spec: SmokePageSpec = page.spec
        let ready: Bool = try await prepare(page)
        guard ready else {
            notes.append("not-instrumented:" + page.rawValue + " [" + language + "/" + theme + "] the page could not be prepared")
            try await finish(page)
            return
        }
        checkClean(spec)
        for requested in sizes {
            let actual: CGSize = try await resize(to: requested)
            currentSize = actual
            requestedText = sizeText(requested)
            await settle(page.rawValue)
            let before: Int = violations.count
            auditLayout(spec, width: actual.width)
            try await inspect(page, spec: spec, width: actual.width, primary: isPrimary(requested))
            let savePNG: Bool = requested.width < 800 || isPrimary(requested)
            let name: String = page.rawValue + "-" + sizeText(requested) + "-" + theme + "-" + language
            let shot: SmokeShot = try await capture(saveAs: savePNG ? name : nil)
            let stats: SmokePixelStats = auditPixels(shot, spec: spec)
            if isPrimary(requested) { try await primaryExtras(page, spec: spec) }
            let added: Int = violations.count - before
            cells.append(SmokeCell(page: page.rawValue, requested: requestedText, width: Int(actual.width.rounded()), height: Int(actual.height.rounded()), theme: theme, language: language, violations: added, meanLuminance: stats.mean, deviation: stats.deviation))
        }
        currentSize = .zero
        try await finish(page)
    }

    private func isPrimary(_ size: CGSize) -> Bool {
        return abs(size.width - primarySize.width) < 1 && abs(size.height - primarySize.height) < 1
    }

    private func checkClean(_ spec: SmokePageSpec) {
        if let message = model.error {
            if spec.network { warnings.append("[" + context + "] " + pageName + ": " + message) } else { check(false, pageName + ": no error banner after loading (" + message + ")") }
            model.error = nil
        } else if !spec.network {
            check(true, pageName + ": no error banner after loading")
        }
        if let toast = model.toast {
            if spec.network { warnings.append("[" + context + "] " + pageName + ": toast " + toast) } else { check(false, pageName + ": no toast after loading (" + toast + ")") }
            model.toast = nil
        }
    }

    // MARK: Pages

    private func prepare(_ page: SmokePage) async throws -> Bool {
        switch page {
        case .search:
            model.select(.movies)
            model.openSearch()
            return true
        case .featured:
            model.select(.featured)
            await model.loadFeatured()
            _ = await waitUntil("featured shelves", timeout: 30) { !model.curated.isEmpty }
            let ok: Bool = !model.curated.isEmpty
            check(ok, "Featured resolves real bundled catalog membership")
            languageChecks()
            return ok
        case .movies:
            model.select(.movies)
            await model.loadCatalog()
            _ = await waitUntil("movie list", timeout: 30) { model.list?.items.isEmpty == false }
            let ok: Bool = model.list?.items.isEmpty == false
            check(ok, "Movie catalog loads from the shared snapshot")
            if let first = model.list?.items.first { cards["movie"] = first }
            return ok
        case .series:
            model.select(.series)
            await model.loadCatalog()
            _ = await waitUntil("series list", timeout: 30) { model.list?.items.isEmpty == false }
            let ok: Bool = model.list?.items.isEmpty == false
            check(ok, "Series catalog is available through the same engine")
            if let first = model.list?.items.first { cards["series"] = first }
            return ok
        case .gamesHome:
            model.select(.games)
            _ = await waitUntil("game shelves", timeout: 25) { !model.gameShelves.isEmpty }
            check(!model.gameShelves.isEmpty, "The games home lists the Steam shelves")
            return true
        case .gamesGrid:
            model.select(.games)
            model.filter.genre = "all"
            await model.loadCatalog()
            _ = await waitUntil("game list", timeout: 30) { model.list?.items.isEmpty == false }
            let ok: Bool = model.list?.items.isEmpty == false
            check(ok, "The Windows game catalog uses real Steam entries on macOS")
            if let first = model.list?.items.first { cards["game"] = first }
            return ok
        case .saved:
            guard let movie = cards["movie"] else { return false }
            let before: Bool = model.isSaved(movie)
            await model.save(movie)
            check(model.isSaved(movie) != before, "Saved titles use the shared preference engine")
            if before { await model.save(movie) } else { savedByTest = movie }
            model.select(.saved)
            await model.loadSaved()
            let savedItems: [MediaCard] = model.list?.items ?? []
            let listed: Bool = savedItems.contains(where: { (card: MediaCard) -> Bool in card.id == movie.id })
            check(listed, "The Saved page lists the saved title")
            return true
        case .downloadsEmpty:
            model.transfers.items = []
            model.select(.downloads)
            return true
        case .downloadsQueue:
            model.transfers.items = SmokeFixtures.transfers()
            model.select(.downloads)
            return true
        case .settings:
            model.select(.settings)
            return true
        case .detailMovie:
            guard let card = cards["movie"] else { return false }
            model.select(.movies)
            model.open(card)
            let loaded: Bool = await loadDetail(card)
            personCandidate = model.detail?.people.first
            return loaded
        case .detailSeries:
            guard let card = cards["series"] else { return false }
            model.select(.series)
            model.open(card)
            return await loadDetail(card)
        case .detailGame:
            guard let card = cards["game"] else { return false }
            model.select(.games)
            model.open(card)
            return await loadDetail(card)
        case .person:
            guard let candidate = personCandidate else { return false }
            model.select(.movies)
            model.open(candidate)
            let loaded: Bool = await waitUntil("person page", timeout: 25) { model.personResult(for: candidate) != nil }
            return loaded
        case .award:
            let awardGroup: CuratedGroup? = model.curated.first(where: { (group: CuratedGroup) -> Bool in group.id.hasPrefix("award-") })
            guard let collection = awardGroup?.id ?? model.curated.first?.id else { return false }
            model.select(.featured)
            model.openCollection(collection)
            _ = await waitUntil("collection list", timeout: 25) { model.list != nil }
            return true
        case .folderSheet:
            model.select(.featured)
            model.folderRequired = true
            let shown: Bool = await waitUntil("folder sheet", timeout: 6) { MacSmoke.mainWindow()?.attachedSheet != nil }
            check(shown, "The first-run folder sheet opens")
            return shown
        case .addSheet:
            model.select(.featured)
            model.addTorrent = true
            let shown: Bool = await waitUntil("add-torrent sheet", timeout: 6) { MacSmoke.mainWindow()?.attachedSheet != nil }
            check(shown, "The add-torrent sheet opens")
            if shown, let sheet = MacSmoke.mainWindow()?.attachedSheet, let content = sheet.contentView {
                let fields: [NSTextField] = SmokeViews.all(in: content).compactMap { (view: NSView) -> NSTextField? in view as? NSTextField }
                let editable: Bool = fields.contains { (field: NSTextField) -> Bool in field.isEditable }
                check(editable, "The add-torrent sheet has an editable link field")
            }
            return shown
        }
    }

    private func loadDetail(_ card: MediaCard) async -> Bool {
        let detailLoaded: Bool = await waitUntil("detail of " + card.id, timeout: 25) { model.detail?.item.id == card.id }
        _ = await waitUntil("releases of " + card.id, timeout: 15) { model.releasesOwner == card.id && !model.releasesLoading }
        if !detailLoaded { notes.append("[" + context + "] detail of " + card.id + " did not load; the skeleton state was measured") }
        return true
    }

    /// Runs after the last size of a page: back navigation, closing sheets, and cleaning up.
    private func finish(_ page: SmokePage) async throws {
        switch page {
        case .search:
            model.back()
        case .detailMovie:
            model.back()
            check(model.route == Route.section(.movies), "Back preserves the catalog route")
        case .folderSheet:
            model.folderRequired = false
            let closed: Bool = await waitUntil("folder sheet to close", timeout: 6) { MacSmoke.mainWindow()?.attachedSheet == nil }
            check(closed, "The folder sheet closes")
        case .addSheet:
            model.addTorrent = false
            let closed: Bool = await waitUntil("add-torrent sheet to close", timeout: 6) { MacSmoke.mainWindow()?.attachedSheet == nil }
            check(closed, "The add-torrent sheet closes")
        default:
            break
        }
    }

    private func languageChecks() {
        var offenders: [String] = []
        var latin: [String] = []
        for group in model.curated {
            for text in [group.title, group.description] {
                if language == "en" && SmokeText.hasCyrillic(text) { offenders.append(text) }
                if language == "ru" { latin.append(contentsOf: SmokeText.latinPhrases(in: text)) }
            }
        }
        if language == "en" {
            let detail: String = offenders.isEmpty ? "" : " (Cyrillic: " + offenders.prefix(3).joined(separator: " | ") + ")"
            check(offenders.isEmpty, "Featured labels use authored English translations" + detail)
        } else {
            for phrase in latin { violate("i18n", "Featured group text has an untranslated Latin phrase: " + phrase) }
        }
    }

    // MARK: Window size

    private func resize(to requested: CGSize) async throws -> CGSize {
        let main: NSWindow = try window()
        main.setContentSize(NSSize(width: requested.width, height: requested.height))
        _ = await waitUntil("window resize", timeout: 2) {
            guard let content = MacSmoke.mainWindow()?.contentView else { return false }
            return abs(content.bounds.width - requested.width) < 1.5 && abs(content.bounds.height - requested.height) < 1.5
        }
        guard let content = main.contentView else { throw UpdateError("The window has no content view") }
        let actual: CGSize = content.bounds.size
        if abs(actual.width - requested.width) > 1.5 || abs(actual.height - requested.height) > 1.5 {
            notes.append("[" + context + "] size-constrained: asked for " + sizeText(requested) + ", the window is " + sizeText(actual))
        }
        return actual
    }

    // MARK: Layout audit

    private func auditLayout(_ spec: SmokePageSpec, width: CGFloat) {
        let frames: [String: CGRect] = SettingsLayoutMeasurements.frames
        for id in spec.required where frames[id] == nil {
            violate("layout", "probe " + id + " was not recorded, so the page lost its instrumentation")
        }
        var seen = Set<String>()
        for prefix in spec.prefixes {
            for problem in SettingsLayoutMeasurements.audit(prefix: prefix, minHit: 24, windowWidth: width) where !seen.contains(problem) {
                seen.insert(problem)
                violate("layout", problem)
            }
        }
        if spec.prefixes.isEmpty && spec.required.isEmpty {
            notes.append("[" + context + "] not-instrumented:" + pageName + ": no layout probes on this page, only the scroll view and pixel audits ran")
        }
        guard let main = MacSmoke.mainWindow(), main.attachedSheet == nil, let scroll = SmokeViews.largestScrollView(in: main), let document = scroll.documentView else { return }
        // A classic scroller that appears after the last layout pass takes its width from the viewport before the document is re-laid out.
        let scrollerAllowance: CGFloat = scroll.verticalScroller?.isHidden == false && scroll.scrollerStyle == .legacy ? NSScroller.scrollerWidth(for: .regular, scrollerStyle: .legacy) : 0
        if document.bounds.width > scroll.contentView.bounds.width + scrollerAllowance + 1.5 {
            violate("layout", "the page is wider than its scroll view (content " + String(Int(document.bounds.width)) + " pt, viewport " + String(Int(scroll.contentView.bounds.width)) + " pt)")
        }
    }

    private func inspect(_ page: SmokePage, spec: SmokePageSpec, width: CGFloat, primary: Bool) async throws {
        switch page {
        case .movies:
            inspectBanner("movies")
        case .series:
            inspectBanner("series")
        case .settings:
            try await inspectSettings(width: width, primary: primary)
        case .downloadsQueue:
            inspectDownloads()
        default:
            break
        }
        if spec.toolbar { inspectToolbar(primary: primary) }
    }

    private func inspectBanner(_ section: String) {
        let frames: [String: CGRect] = SettingsLayoutMeasurements.frames
        guard let banner = frames["catalog.banner." + section], let content = MacSmoke.mainWindow()?.contentView else { return }
        let maximumHeight = CatalogHeroMetrics.height(forWidth: banner.width)
        check(banner.width > 280 && banner.height >= 280 && banner.height <= maximumHeight + 2, section + ": spotlight banner has a complete native layout")
        let inside: Bool = banner.minX >= -2 && banner.maxX <= content.bounds.width + 2 && banner.minY >= -2 && banner.maxY <= content.bounds.height + 2
        check(inside, section + ": spotlight banner is visible at the top of the catalog")
    }

    private func reportToolbar(_ primary: Bool, _ ok: Bool, _ text: String) {
        if primary { check(ok, text) } else if !ok { violate("toolbar", text) }
    }

    private func inspectToolbar(primary: Bool) {
        guard let main = MacSmoke.mainWindow() else { return }
        reportToolbar(primary, main.toolbar?.isVisible != true, "toolbar: hidden at the top of the page")
    }

    private func inspectExpandedToolbar() {
        SettingsLayoutMeasurements.refreshNativeViews()
        guard let main = MacSmoke.mainWindow() else { return }
        check(main.toolbar?.isVisible == true, "toolbar: appears after scrolling")
        for id in ["toolbar.add", "toolbar.refresh", "toolbar.appearance"] {
            guard SettingsLayoutMeasurements.nativeAttached[id] == true, let frame = SettingsLayoutMeasurements.nativeFrames[id], let visible = SettingsLayoutMeasurements.nativeVisibleFrames[id] else {
                check(false, "toolbar: missing native control " + id)
                continue
            }
            let fits: Bool = frame.width > 12 && frame.height > 12 && frame.minX >= -2 && frame.maxX <= main.frame.width + 2 && frame.minY >= -2 && frame.maxY <= main.frame.height + 2
            check(fits, "toolbar: " + id + " fits the native window")
            let shown: CGRect = frame.intersection(visible)
            let unclipped: Bool = !shown.isNull && shown.width * shown.height >= frame.width * frame.height * 0.9
            check(unclipped, "toolbar: " + id + " is visible and not clipped by its native host")
        }
    }

    // MARK: Settings

    private func settingsScrollView() throws -> NSScrollView {
        guard let content = MacSmoke.mainWindow()?.contentView else { throw UpdateError("Settings window is missing") }
        let scrolls: [NSScrollView] = SmokeViews.all(in: content).compactMap { (view: NSView) -> NSScrollView? in view as? NSScrollView }
        let tall: [NSScrollView] = scrolls.filter { (scroll: NSScrollView) -> Bool in
            scroll.bounds.width > 300 && (scroll.documentView?.bounds.height ?? 0) > scroll.contentView.bounds.height + 10
        }
        guard let best = tall.max(by: { (a: NSScrollView, b: NSScrollView) -> Bool in (a.documentView?.bounds.height ?? 0) < (b.documentView?.bounds.height ?? 0) }) else {
            throw UpdateError("The settings page must scroll to expose all sections")
        }
        return best
    }

    private func scrollSettings(toBottom: Bool) async throws {
        let scroll: NSScrollView = try settingsScrollView()
        guard let document = scroll.documentView else { throw UpdateError("The settings scroll document is missing") }
        // Unified toolbars give the scroll view a top inset. Its natural leading origin is negative;
        // forcing zero would put the header underneath the toolbar even though nobody scrolled.
        let insets: NSEdgeInsets = scroll.contentInsets
        let low: CGFloat = document.bounds.minY - (document.isFlipped ? insets.top : insets.bottom)
        let high: CGFloat = max(low, document.bounds.maxY - scroll.contentView.bounds.height + (document.isFlipped ? insets.bottom : insets.top))
        let y: CGFloat = toBottom == document.isFlipped ? high : low
        scroll.contentView.scroll(to: NSPoint(x: document.bounds.minX - insets.left, y: y))
        scroll.reflectScrolledClipView(scroll.contentView)
        await settle("settings scroll", timeout: 3)
    }

    private func toolbarBottom(of main: NSWindow) -> CGFloat {
        guard let content = main.contentView else { return 0 }
        return max(0, content.bounds.height - main.contentLayoutRect.maxY)
    }

    private func inspectSettings(width: CGFloat, primary: Bool) async throws {
        let main: NSWindow = try window()
        let frames: [String: CGRect] = SettingsLayoutMeasurements.frames
        guard let viewport = frames["viewport"], let header = frames["header"] else { return }
        let expected: [String] = SettingsSectionID.allCases.map { (section: SettingsSectionID) -> String in "card." + section.rawValue }
        let measured: Set<String> = Set(frames.keys.filter { (id: String) -> Bool in id.hasPrefix("card.") })
        check(measured == Set(expected), "settings: every section card is measured")
        let present: [String] = expected.filter { (id: String) -> Bool in frames[id] != nil }
        let ordered: [String] = present.sorted { (a: String, b: String) -> Bool in (frames[a]?.minY ?? 0) < (frames[b]?.minY ?? 0) }
        check(ordered == present, "settings: the cards appear in the declared order")
        let cards: [CGRect] = ordered.compactMap { (id: String) -> CGRect? in frames[id] }
        guard let firstCard = cards.first else { return }
        let fits: Bool = cards.allSatisfy { (card: CGRect) -> Bool in card.width > 280 && card.height > 30 && card.minX >= viewport.minX - 2 && card.maxX <= viewport.maxX + 2 }
        check(fits, "settings: all settings cards fit horizontally")
        let aligned: Bool = cards.allSatisfy { (card: CGRect) -> Bool in abs(card.minX - firstCard.minX) < 2 && abs(card.width - firstCard.width) < 2 }
        check(aligned, "settings: settings cards share one leading edge and width")
        var continuous: Bool = true
        for pair in zip(cards, cards.dropFirst()) {
            if !(pair.1.minY >= pair.0.maxY - 2 && pair.1.minY - pair.0.maxY < 80) { continuous = false }
        }
        check(continuous, "settings: settings cards form one continuous vertical column")
        let top: CGFloat = toolbarBottom(of: main)
        let headerInside: Bool = header.minY >= top - 1 && header.maxY <= viewport.maxY + 2 && header.minX >= viewport.minX - 2 && header.maxX <= viewport.maxX + 2
        check(headerInside, "settings: the full settings header is visible below the toolbar")
        let firstVisible: Bool = firstCard.minY >= top - 1 && firstCard.minY < viewport.maxY && firstCard.intersection(viewport).height > 30
        check(firstVisible, "settings: first settings section is visible below the toolbar")
        let hasNavigation: Bool = frames["settings.nav"] != nil
        if width <= 1000 { check(!hasNavigation, "settings: narrow windows use a single column without the section list") }
        if width >= 1500 { check(hasNavigation, "settings: wide windows show the section list") }
        try await scrollSettings(toBottom: true)
        SettingsLayoutMeasurements.refreshNativeViews()
        let scrolled: [String: CGRect] = SettingsLayoutMeasurements.frames
        if let lastID = present.last, let last = scrolled[lastID], let scrolledViewport = scrolled["viewport"] {
            let reachable: Bool = last.maxY <= scrolledViewport.maxY + 3 && last.intersection(scrolledViewport).height >= min(last.height, scrolledViewport.height) * 0.8
            check(reachable, "settings: final settings section is reachable by scrolling")
        }
        if primary { _ = try await capture(saveAs: "settings-" + theme + "-" + language + "-bottom") }
        try await scrollSettings(toBottom: false)
    }

    // MARK: Downloads

    private func inspectDownloads() {
        let frames: [String: CGRect] = SettingsLayoutMeasurements.frames
        var rows: [CGRect] = []
        for index in 0..<5 {
            if let frame = frames["downloads.row." + String(index)] { rows.append(frame) }
        }
        // The queue is a lazy list, so only the rows that were realised at this window size can be measured.
        if rows.count >= 2 {
            var ordered: Bool = true
            for index in 1..<rows.count where rows[index].minY < rows[index - 1].maxY - 1 { ordered = false }
            check(ordered, "downloads: queue rows are stacked without overlapping")
        }
        if !rows.isEmpty {
            check(rows.allSatisfy { (row: CGRect) -> Bool in row.height >= 40 }, "downloads: every queue row is tall enough for its content")
        }
        if rows.count < 5 {
            note("not-instrumented:downloads-lazy-rows: only " + String(rows.count) + " of 5 queue rows were realised at this size")
        }
        guard let main = MacSmoke.mainWindow() else { return }
        let nodes: [SmokeAccessibilityNode] = SmokeAccessibility.nodes(in: main)
        let controlRoles: Set<String> = [NSAccessibility.Role.button.rawValue, NSAccessibility.Role.popUpButton.rawValue, NSAccessibility.Role.menuButton.rawValue]
        let controls: [SmokeAccessibilityNode] = nodes.filter { (node: SmokeAccessibilityNode) -> Bool in controlRoles.contains(node.role) && node.frame.width > 0 && node.frame.height > 0 }
        let pauseLabels: Set<String> = ["Пауза", "Продолжить", "Pause", "Resume"]
        let pauseNodes: [SmokeAccessibilityNode] = controls.filter { (node: SmokeAccessibilityNode) -> Bool in pauseLabels.contains(node.label) }
        if pauseNodes.isEmpty {
            note("not-instrumented:downloads-pause-buttons: no pause or resume button found in the accessibility tree (" + String(nodes.count) + " nodes)")
        } else {
            check(pauseNodes.allSatisfy { (node: SmokeAccessibilityNode) -> Bool in node.frame.width >= 23.5 && node.frame.height >= 23.5 }, "downloads: pause and resume targets are at least 24 pt")
        }
        notes.append("[" + context + "] not-instrumented:downloads-truncation: truncated text cannot be read from the accessibility tree")
        for (index, row) in rows.enumerated() {
            let area: CGRect = SmokeViews.screenRect(forGlobal: row, in: main).insetBy(dx: -2, dy: -2)
            let inside: [SmokeAccessibilityNode] = controls.filter { (node: SmokeAccessibilityNode) -> Bool in area.contains(CGPoint(x: node.frame.midX, y: node.frame.midY)) }
            for control in inside where !area.contains(control.frame) {
                violate("layout", "downloads.row." + String(index) + ": control '" + control.label + "' extends outside its row")
            }
            for first in 0..<inside.count {
                for second in (first + 1)..<max(first + 1, inside.count) {
                    let a: CGRect = inside[first].frame
                    let b: CGRect = inside[second].frame
                    let overlap: CGRect = a.intersection(b)
                    let nested: Bool = a.contains(b) || b.contains(a)
                    if !overlap.isNull && overlap.width * overlap.height > 1 && !nested {
                        violate("layout", "downloads.row." + String(index) + ": controls '" + inside[first].label + "' and '" + inside[second].label + "' overlap")
                    }
                }
            }
        }
    }

    // MARK: Screenshots and pixels

    private func capture(saveAs name: String?) async throws -> SmokeShot {
        await pause(150)
        let main: NSWindow = try window()
        let target: NSWindow = main.attachedSheet ?? main
        guard let content = target.contentView else { throw UpdateError("The native application window is missing") }
        let view: NSView = content.superview ?? content
        main.makeKeyAndOrderFront(nil)
        view.layoutSubtreeIfNeeded()
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw UpdateError("Could not capture the native content view") }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        if let name {
            guard let png = bitmap.representation(using: .png, properties: [:]) else { throw UpdateError("Could not encode the native screenshot") }
            try png.write(to: root.appendingPathComponent(name + ".png"))
        }
        return SmokeShot(bitmap: bitmap, scale: target.backingScaleFactor, bounds: view.bounds, isSheet: target !== main)
    }

    private func auditPixels(_ shot: SmokeShot, spec: SmokePageSpec) -> SmokePixelStats {
        let expectedWidth: Int = Int((shot.bounds.width * shot.scale).rounded())
        let expectedHeight: Int = Int((shot.bounds.height * shot.scale).rounded())
        if abs(shot.bitmap.pixelsWide - expectedWidth) > 2 || abs(shot.bitmap.pixelsHigh - expectedHeight) > 2 {
            let actualText: String = "\(shot.bitmap.pixelsWide)x\(shot.bitmap.pixelsHigh)"
            let expectedText: String = "\(expectedWidth)x\(expectedHeight)"
            violate("pixel", "the screenshot is \(actualText) px but the window is \(expectedText) px")
        }
        let region: CGRect
        if shot.isSheet {
            region = shot.bounds
        } else {
            let guardWidth: CGFloat = min(260, shot.bounds.width * 0.3)
            region = CGRect(x: guardWidth, y: 60, width: max(1, shot.bounds.width - guardWidth), height: max(1, shot.bounds.height - 60))
        }
        let stats: SmokePixelStats = SmokePixelAudit.measure(shot.bitmap, scale: shot.scale, region: region)
        if stats.samples == 0 {
            violate("pixel", "the screenshot could not be sampled")
            return stats
        }
        if stats.deviation < 0.02 { violate("pixel", "the screenshot is a flat fill (luminance deviation " + String(format: "%.3f", stats.deviation) + ")") }
        if theme == "dark" {
            if stats.darkShare < 0.25 { violate("pixel", "dark appearance did not take effect (only " + String(Int(stats.darkShare * 100)) + "% dark pixels)") }
            if spec.plain && stats.mean >= 0.30 { violate("pixel", "dark page is too bright (mean luminance " + String(format: "%.2f", stats.mean) + ")") }
        } else {
            if stats.lightShare < 0.10 { violate("pixel", "light appearance did not take effect (only " + String(Int(stats.lightShare * 100)) + "% light pixels)") }
            if spec.plain && stats.mean <= 0.65 { violate("pixel", "light page is too dark (mean luminance " + String(format: "%.2f", stats.mean) + ")") }
        }
        if !shot.isSheet {
            let key: Int = Int(currentSize.width.rounded())
            if let previous = lastDigest[key], previous.page != pageName, previous.signature == stats.signature {
                violate("pixel", "the page looks identical to the previous page " + previous.page)
            }
            lastDigest[key] = (pageName, stats.signature)
        }
        return stats
    }

    // MARK: Accessibility, key loop, performance (primary size only)

    private func primaryExtras(_ page: SmokePage, spec: SmokePageSpec) async throws {
        accessibilityPass(spec)
        dumpLayout(page)
        guard language == "ru" && theme == "dark" else { return }
        switch page {
        case .search:
            if let frame = SettingsLayoutMeasurements.frames["search.field"] {
                check(frame.width >= 220 && frame.height >= 40, "search: the field is visible and large enough")
            } else { check(false, "search: the field is missing") }
            check(MacSmoke.mainWindow()?.firstResponder is NSTextView, "search: opening the page focuses the input")
            keyViewLoop()
        case .featured:
            try await scrollStall("featured")
        case .movies:
            try await scrollStall("movies")
        default:
            break
        }
    }

    private func accessibilityPass(_ spec: SmokePageSpec) {
        guard let main = MacSmoke.mainWindow() else { return }
        let target: NSWindow = main.attachedSheet ?? main
        let nodes: [SmokeAccessibilityNode] = SmokeAccessibility.nodes(in: target)
        metrics["ax.nodes." + pageName] = Double(nodes.count)
        if nodes.count < 8 {
            notes.append("[" + context + "] not-instrumented:ax-tree:" + pageName + " the accessibility tree has " + String(nodes.count) + " nodes; checking NSControl labels instead")
            guard let content = target.contentView else { return }
            for view in SmokeViews.all(in: content) {
                guard let control = view as? NSControl, !control.isHiddenOrHasHiddenAncestor else { continue }
                if control is NSScroller || control is NSTableView { continue }
                // SwiftUI bridges (switches, pop-ups, segmented controls) take their labels from SwiftUI semantics, which the NSControl cannot report.
                if String(describing: type(of: control)).hasPrefix("SwiftUI") || String(describing: type(of: control)) == "PlatformSwitch" { continue }
                let label: String = control.accessibilityLabel() ?? ""
                let title: String = (control as? NSButton)?.title ?? ""
                if label.isEmpty && title.isEmpty && !(control is NSTextField) {
                    violate("a11y", String(describing: type(of: control)) + " has no accessible label")
                }
            }
            return
        }
        for node in nodes where SmokeAccessibility.interactiveRoles.contains(node.role) {
            let text: String = node.label.trimmingCharacters(in: .whitespacesAndNewlines)
            let place: String = " at " + String(Int(node.frame.minX)) + "," + String(Int(node.frame.minY))
            if text.isEmpty {
                violate("a11y", node.role + " has no accessible label" + place)
            } else if SmokeText.looksLikeSymbolName(text) {
                violate("a11y", node.role + " is announced by its symbol name '" + text + "'" + place)
            }
        }
        guard spec.scansText else { return }
        for node in nodes where !node.label.isEmpty {
            if language == "en" && SmokeText.hasCyrillic(node.label) && node.label != "Русский" {
                violate("i18n", "Russian text in the English interface: " + node.label)
            }
            if language == "ru" {
                for phrase in SmokeText.latinPhrases(in: node.label) { violate("i18n", "Latin phrase in the Russian interface: " + phrase) }
            }
        }
    }

    private func dumpLayout(_ page: SmokePage) {
        var report: [String: [String: Double]] = [:]
        for (id, frame) in SettingsLayoutMeasurements.frames {
            report[id] = ["x": Double(frame.minX), "y": Double(frame.minY), "width": Double(frame.width), "height": Double(frame.height)]
        }
        guard let data = try? JSONEncoder().encode(report) else { return }
        try? data.write(to: root.appendingPathComponent("layout-" + page.rawValue + "-" + theme + "-" + language + ".json"))
    }

    /// Tab through the key-view loop and record what the focus visits.
    private func keyViewLoop() {
        guard let main = MacSmoke.mainWindow() else { return }
        main.makeFirstResponder(nil)
        var visited: [String] = []
        var identities = Set<ObjectIdentifier>()
        var reachedSearch: Bool = false
        var hiddenVisited: Int = 0
        for _ in 0..<80 {
            main.selectNextKeyView(nil)
            guard var view = main.firstResponder as? NSView else { continue }
            if let editor = view as? NSTextView, let owner = editor.delegate as? NSView { view = owner }
            if identities.contains(ObjectIdentifier(view)) { continue }
            identities.insert(ObjectIdentifier(view))
            visited.append(String(describing: type(of: view)))
            if let field = view as? NSTextField, field.isEditable { reachedSearch = true }
            if view.alphaValue == 0 || view.isHiddenOrHasHiddenAncestor { hiddenVisited += 1 }
        }
        if let data = try? JSONEncoder().encode(visited) { try? data.write(to: root.appendingPathComponent("keyviews.json")) }
        metrics["keyview.distinct"] = Double(identities.count)
        if identities.count < 2 {
            note("not-instrumented:keyview-loop: the key-view loop did not move (" + String(identities.count) + " distinct views)")
            return
        }
        check(hiddenVisited == 0, "keyboard: no invisible view is part of the key-view loop")
        if reachedSearch {
            check(true, "keyboard: the search page input is part of the key-view loop")
        } else {
            violate("keyboard", "the search page input was not reached by Tab (keyboard UI mode " + String(UserDefaults.standard.integer(forKey: "AppleKeyboardUIMode")) + ")")
        }
        if UserDefaults.standard.integer(forKey: "AppleKeyboardUIMode") == 0 {
            notes.append("[" + context + "] not-instrumented:keyview-controls: Full Keyboard Access is off, so only text fields and lists are in the loop")
        }
    }

    /// Scrolls the page in 40 steps while a timer measures main thread gaps.
    private func scrollStall(_ label: String) async throws {
        let main: NSWindow = try window()
        guard let scroll = SmokeViews.largestScrollView(in: main), let document = scroll.documentView else {
            notes.append("[" + context + "] not-instrumented:scroll-" + label + ": no scroll view found")
            return
        }
        let insets: NSEdgeInsets = scroll.contentInsets
        let low: CGFloat = document.bounds.minY - (document.isFlipped ? insets.top : insets.bottom)
        let high: CGFloat = max(low, document.bounds.maxY - scroll.contentView.bounds.height + (document.isFlipped ? insets.bottom : insets.top))
        guard high - low > 200 else {
            notes.append("[" + context + "] not-instrumented:scroll-" + label + ": the page is shorter than the window")
            return
        }
        let topY: CGFloat = document.isFlipped ? low : high
        let bottomY: CGFloat = document.isFlipped ? high : low
        let monitor = SmokeStallMonitor()
        monitor.start()
        for step in 1...40 {
            let y: CGFloat = topY + (bottomY - topY) * CGFloat(step) / 40
            scroll.contentView.scroll(to: NSPoint(x: document.bounds.minX - insets.left, y: y))
            scroll.reflectScrolledClipView(scroll.contentView)
            await pause(16)
        }
        let result = monitor.stop()
        if label == "movies" {
            await pause(180)
            inspectExpandedToolbar()
        }
        scroll.contentView.scroll(to: NSPoint(x: document.bounds.minX - insets.left, y: topY))
        scroll.reflectScrolledClipView(scroll.contentView)
        metrics["scroll." + label + ".maxGapMs"] = result.maxGap
        metrics["scroll." + label + ".gapsOver100ms"] = Double(result.over100)
        let slowRunner: Bool = smokeIsIntel()
        if slowRunner && (result.maxGap >= 600 || result.over100 >= 8) { notes.append("[" + context + "] perf-note: scrolling " + label + " on an Intel runner had " + String(result.over100) + " stalls, longest " + String(Int(result.maxGap)) + " ms") }
        if !slowRunner && result.maxGap >= 600 { violate("perf", "scrolling " + label + " froze the main thread for " + String(Int(result.maxGap)) + " ms") }
        if !slowRunner && result.over100 >= 8 { violate("perf", "scrolling " + label + " had " + String(result.over100) + " stalls over 100 ms") }
        if result.maxGap < 600 && result.over100 < 8 { check(true, "perf: scrolling " + label + " keeps the main thread responsive") }
        await settle("after scrolling " + label, timeout: 3)
        if label == "movies" { check(main.toolbar?.isVisible != true, "toolbar: hides again on return to the top") }
    }

    // MARK: End of the run

    private func finalChecks() {
        currentSize = .zero
        pageName = "final"
        let resident: Double = smokeResidentMegabytes()
        metrics["residentMB"] = resident
        if resident > 600 { violate("perf", "the application uses " + String(Int(resident)) + " MB of resident memory") } else { checkGlobal(true, "perf: resident memory stays under 600 MB") }
        checkGlobal(!cells.isEmpty, "The layout matrix produced measurements (" + String(cells.count) + " cells)")
    }

    private func restoreDefaults() async {
        model.transfers.items = []
        model.folderRequired = false
        model.addTorrent = false
        if let card = savedByTest, model.isSaved(card) { await model.save(card) }
        await model.set("uiLanguage", .string("ru"))
        model.setAppearance(AppearanceMode.dark)
        if let main = MacSmoke.mainWindow() { main.setContentSize(NSSize(width: 1180, height: 760)) }
    }

    private func writeReports(fatal: String?) async {
        let artwork = JSONValue.object([
            "loaded": .number(Double(ImageStore.shared.loadedCount)),
            "failed": .number(Double(ImageStore.shared.failedCount)),
            "errors": .array(await ArtworkDiagnostics.shared.snapshot().map(JSONValue.string))
        ])
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let report = SmokeReport(checks: checks, failures: failures, notes: notes, warnings: warnings, metrics: metrics, environment: environment)
        if let data = try? encoder.encode(artwork) { try? data.write(to: root.appendingPathComponent("artwork.json")) }
        if let data = try? encoder.encode(violations) { try? data.write(to: root.appendingPathComponent("layout-violations.json")) }
        if let data = try? encoder.encode(cells) { try? data.write(to: root.appendingPathComponent("matrix.json")) }
        if let data = try? encoder.encode(report) { try? data.write(to: root.appendingPathComponent("checks.json")) }
        var lines: [String] = failures
        if let fatal { lines.insert(fatal, at: 0) }
        if !lines.isEmpty {
            try? lines.joined(separator: "\n").write(to: root.appendingPathComponent("error.txt"), atomically: true, encoding: .utf8)
        }
    }
}

private func smokeIsIntel() -> Bool {
    #if arch(x86_64)
    return true
    #else
    return false
    #endif
}
