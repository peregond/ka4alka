import AppKit
import SwiftUI
import XCTest
@testable import Ka4alka

// MARK: Shared test helpers

/// Locations of the source files, so tests can grep them (probe ids, localisation, accessibility settings).
enum TestSources {
    /// The `macos` folder of the repository.
    static var macos: URL {
        return URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    }
    static var sources: URL { return macos.appendingPathComponent("Sources/Ka4alka") }
    static var repository: URL { return macos.deletingLastPathComponent() }

    static func swiftFiles() throws -> [(name: String, text: String)] {
        let urls: [URL] = try FileManager.default.contentsOfDirectory(at: sources, includingPropertiesForKeys: nil)
        var files: [(name: String, text: String)] = []
        for url in urls.sorted(by: { (a: URL, b: URL) -> Bool in a.lastPathComponent < b.lastPathComponent }) where url.pathExtension == "swift" {
            let text: String = try String(contentsOf: url, encoding: .utf8)
            files.append((name: url.lastPathComponent, text: text))
        }
        return files
    }

    static func text(of name: String) throws -> String {
        return try String(contentsOf: sources.appendingPathComponent(name), encoding: .utf8)
    }
}

/// Tests change the interface language through UserDefaults; they put it back afterwards.
enum TestLanguage {
    static func current() -> String? { return UserDefaults.standard.string(forKey: "UiLanguage") }
    static func restore(_ value: String?) {
        if let value { UserDefaults.standard.set(value, forKey: "UiLanguage") } else { UserDefaults.standard.removeObject(forKey: "UiLanguage") }
    }
}

/// JSON payloads in the shape the engine sends, and the models decoded from them.
enum TestFixtures {
    static func cardJSON(id: String, title: String, section: String = "movies", extra: String = "") -> String {
        return "{\"id\":\"" + id + "\",\"title\":\"" + title + "\",\"section\":\"" + section + "\",\"year\":2024,\"rating\":\"7.8\",\"poster\":null,\"description\":\"Описание\",\"genre\":\"драма\",\"quality\":\"Full HD\",\"saved\":false,\"platform\":null" + extra + "}"
    }

    static let settingsJSON: String = #"{"folder":"/tmp/ka4alka-test","folderConfigured":true,"uiLanguage":"ru","light":false,"liteMode":false,"autoResumeDownloads":false,"autoRecoverDownloads":false,"notifyDownloads":false,"checkForUpdates":false,"autoUpdate":false,"hidePoorQuality":false,"minimumReleaseHeight":0,"maxDownloadKbps":0,"maxUploadKbps":0,"lanEnabled":false,"downloadSort":"added","hidden":[],"hiddenGenres":[],"savedIds":[]}"#

    static func releaseJSON(id: String, title: String, extra: String = "") -> String {
        return "{\"id\":\"" + id + "\",\"title\":\"" + title + "\",\"source\":\"Test\",\"size\":2147483648,\"seeds\":42,\"leechers\":3,\"quality\":\"Full HD\",\"type\":\"WEB-DL\",\"audio\":\"RU\",\"studios\":[\"Studio\"],\"audioTypes\":[\"Dub\"],\"codec\":\"H.264\",\"subs\":\"\",\"hdr\":\"\",\"poor\":false,\"downloadable\":true,\"recommended\":true,\"multipleSeasons\":false,\"multipleEpisodes\":false,\"englishAudio\":false,\"russianAudio\":true" + extra + "}"
    }

    static func decode<T: Decodable>(_ type: T.Type, _ json: String) throws -> T {
        return try JSONDecoder().decode(type, from: Data(json.utf8))
    }

    static func card(_ id: String, _ title: String, section: String = "movies") throws -> MediaCard {
        return try decode(MediaCard.self, cardJSON(id: id, title: title, section: section))
    }
}

// MARK: Hosting pages offscreen

private struct LayoutCase {
    var name: String
    var build: (AppModel) -> AnyView
}

private struct LayoutOutcome {
    var problems: [String]
    var scrollViewFound: Bool
}

@MainActor final class LayoutTests: XCTestCase {
    // MARK: Fixtures

    private func makeModel() throws -> AppModel {
        let model = AppModel()
        model.bridge.disabled = true
        model.settings = try TestFixtures.decode(AppSettings.self, TestFixtures.settingsJSON)
        model.connected = true
        model.version = "0.0.0"
        let cards: String = [
            TestFixtures.cardJSON(id: "movies:one", title: "Первый фильм"),
            TestFixtures.cardJSON(id: "movies:two", title: "A very long title that has to wrap onto several lines inside a narrow poster card"),
            TestFixtures.cardJSON(id: "series:three", title: "Сериал", section: "series"),
            TestFixtures.cardJSON(id: "games:four", title: "Игра", section: "games", extra: ",\"store\":{\"price\":\"999 ₽\",\"originalPrice\":\"1 999 ₽\",\"discount\":50,\"free\":false,\"reviewPercent\":91}")
        ].joined(separator: ",")
        let groups: String = "[{\"id\":\"lead\",\"title\":\"Главное\",\"description\":\"Описание подборки\",\"layout\":\"lead\",\"items\":[" + cards + "],\"count\":4},{\"id\":\"shelf\",\"title\":\"Подборка\",\"description\":\"\",\"layout\":\"shelf\",\"items\":[" + cards + "],\"count\":4}]"
        model.curated = try TestFixtures.decode([CuratedGroup].self, groups)
        model.list = try TestFixtures.decode(CatalogResult.self, "{\"items\":[" + cards + "],\"hasNext\":false}")
        let queue: String = "[{\"id\":\"q1\",\"title\":\"Фильм\",\"releaseTitle\":\"Film.2024.1080p\",\"progress\":42.5,\"status\":\"Качается\",\"hint\":\"\",\"paused\":false,\"complete\":false,\"missing\":false,\"lowSpace\":false,\"downloadRate\":3400000,\"uploadRate\":0,\"totalBytes\":4200000000,\"addedUtc\":\"2026-10-01T10:00:00Z\",\"folder\":\"/tmp\",\"canUpdateRelease\":false,\"peers\":\"3\",\"files\":2,\"statusKind\":\"downloading\"},{\"id\":\"q2\",\"title\":\"Без размера\",\"totalBytes\":null,\"paused\":true}]"
        model.transfers.items = try TestFixtures.decode([Transfer].self, queue)
        return model
    }

    private func makeCases(for model: AppModel) throws -> [LayoutCase] {
        let card: MediaCard = try TestFixtures.card("movies:one", "Первый фильм")
        let detail: DetailResult = try TestFixtures.decode(DetailResult.self, "{\"item\":" + TestFixtures.cardJSON(id: "movies:one", title: "Первый фильм") + ",\"people\":[],\"awards\":[]}")
        model.detail = detail
        model.releasesOwner = card.id
        let row: ReleaseRow = try TestFixtures.decode(ReleaseRow.self, TestFixtures.releaseJSON(id: "r1", title: "Film.2024.1080p.WEB-DL"))
        model.releases = [row]
        model.section = .movies
        return [
            LayoutCase(name: "search", build: { (_: AppModel) -> AnyView in AnyView(SearchView()) }),
            LayoutCase(name: "settings", build: { (_: AppModel) -> AnyView in AnyView(SettingsView()) }),
            LayoutCase(name: "downloads", build: { (m: AppModel) -> AnyView in AnyView(DownloadsView(store: m.transfers)) }),
            LayoutCase(name: "featured", build: { (_: AppModel) -> AnyView in AnyView(FeaturedView()) }),
            LayoutCase(name: "catalog", build: { (_: AppModel) -> AnyView in AnyView(CatalogView()) }),
            LayoutCase(name: "detail", build: { (_: AppModel) -> AnyView in AnyView(DetailView(card: card)) })
        ]
    }

    private func requireWindowServer() throws {
        _ = NSApplication.shared
        if NSScreen.screens.isEmpty { throw XCTSkip("This runner has no display, so SwiftUI pages cannot be laid out.") }
    }

    // MARK: Hosting

    private func render(_ content: AnyView, model: AppModel, width: CGFloat, dark: Bool, rtl: Bool) -> LayoutOutcome {
        let priorProbeMode = SettingsLayoutMeasurements.enabledForTests
        SettingsLayoutMeasurements.enabledForTests = true
        SettingsLayoutMeasurements.frames.removeAll()
        defer { SettingsLayoutMeasurements.enabledForTests = priorProbeMode }
        let updater = MacUpdater()
        let direction: LayoutDirection = rtl ? .rightToLeft : .leftToRight
        let scheme: ColorScheme = dark ? .dark : .light
        let root = AnyView(content
            .environmentObject(model)
            .environmentObject(updater)
            .environment(\.layoutDirection, direction)
            .preferredColorScheme(scheme))
        let controller = NSHostingController(rootView: root)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: width, height: 800), styleMask: [.borderless], backing: .buffered, defer: false)
        window.appearance = NSAppearance(named: dark ? NSAppearance.Name.darkAqua : NSAppearance.Name.aqua)
        window.contentViewController = controller
        window.setContentSize(NSSize(width: width, height: 800))
        controller.view.frame = NSRect(x: 0, y: 0, width: width, height: 800)
        controller.view.layoutSubtreeIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.08))
        controller.view.layoutSubtreeIfNeeded()
        var problems: [String] = controlProblems(in: controller.view)
        let menuFrames = SettingsLayoutMeasurements.frames.filter { $0.key.hasPrefix("menu.") }
        let nativeMenus = SmokeViews.all(in: controller.view).contains { $0 is NSPopUpButton }
        if nativeMenus && menuFrames.isEmpty {
            problems.append("Menu hit-area probes were not recorded")
        }
        problems.append(contentsOf: SettingsLayoutMeasurements.audit(prefix: "menu.", minHit: 24, windowWidth: width))
        var found = false
        if let scroll = largestScrollView(in: controller.view), let document = scroll.documentView {
            found = true
            if document.bounds.width > scroll.contentView.bounds.width + 1.5 {
                problems.append("content is " + String(Int(document.bounds.width)) + " pt wide in a " + String(Int(scroll.contentView.bounds.width)) + " pt viewport")
            }
        }
        window.contentViewController = nil
        return LayoutOutcome(problems: problems, scrollViewFound: found)
    }

    private func largestScrollView(in root: NSView) -> NSScrollView? {
        let scrolls: [NSScrollView] = SmokeViews.all(in: root).compactMap { (view: NSView) -> NSScrollView? in view as? NSScrollView }
        return scrolls.max { (a: NSScrollView, b: NSScrollView) -> Bool in
            a.contentView.bounds.width * a.contentView.bounds.height < b.contentView.bounds.width * b.contentView.bounds.height
        }
    }

    /// Native controls must be visible, at least as wide as they want to be, and not collapsed.
    /// Most SwiftUI controls are drawn by SwiftUI itself; this covers the ones that are real NSControls.
    /// The smallest side is 16 pt here because checkboxes and switches are 16 to 18 pt tall natively.
    private func controlProblems(in root: NSView) -> [String] {
        var problems: [String] = []
        for view in SmokeViews.all(in: root) {
            guard let control = view as? NSControl, !control.isHiddenOrHasHiddenAncestor else { continue }
            let relevant: Bool = control is NSButton || control is NSSegmentedControl || control is NSSearchField
            if !relevant || control.bounds.width <= 0 { continue }
            let name: String = String(describing: type(of: control))
            let intrinsic: CGFloat = control.intrinsicContentSize.width
            if intrinsic > 0 && control.bounds.width < intrinsic - 1 {
                problems.append(name + " is " + String(Int(control.bounds.width)) + " pt wide but needs " + String(Int(intrinsic)) + " pt")
            }
            // A pop-up button hosts a SwiftUI label that sets its own hit area, so only the plain buttons and fields are held to the minimum side.
            if !(control is NSPopUpButton) && min(control.bounds.width, control.bounds.height) < 16 {
                problems.append(name + " is smaller than 16 pt (" + String(Int(control.bounds.width)) + "x" + String(Int(control.bounds.height)) + ")")
            }
        }
        return problems
    }

    // MARK: Tests

    func testReadyUpdateFooterFitsShortWindows() throws {
        try requireWindowServer()
        let priorProbeMode = SettingsLayoutMeasurements.enabledForTests
        let savedLanguage = TestLanguage.current()
        SettingsLayoutMeasurements.enabledForTests = true
        defer {
            SettingsLayoutMeasurements.enabledForTests = priorProbeMode
            TestLanguage.restore(savedLanguage)
        }
        for language in ["ru", "en"] {
            UserDefaults.standard.set(language, forKey: "UiLanguage")
            for height: CGFloat in [468, 520, 700] {
                let model = try makeModel()
                model.version = "0.44.12"
                let updater = MacUpdater()
                updater.available = true; updater.ready = true
                updater.statusText = tr("Обновление готово к установке", "Update ready to install")
                SettingsLayoutMeasurements.frames.removeAll()
                let controller = NSHostingController(rootView: RootView().environmentObject(model).environmentObject(updater))
                let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 760, height: height), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
                window.contentViewController = controller
                window.setContentSize(NSSize(width: 760, height: height))
                controller.view.frame = NSRect(x: 0, y: 0, width: 760, height: height)
                controller.view.layoutSubtreeIfNeeded()
                RunLoop.main.run(until: Date().addingTimeInterval(0.2))
                controller.view.layoutSubtreeIfNeeded()
                for id in ["sidebar.update.action", "sidebar.update.status", "sidebar.update.version"] {
                    guard let frame = SettingsLayoutMeasurements.frames[id] else {
                        XCTFail("Missing " + id + " " + language); continue
                    }
                    XCTAssertGreaterThan(frame.height, 0, id)
                    XCTAssertGreaterThanOrEqual(frame.minY, -2, id + " " + language)
                    XCTAssertLessThanOrEqual(frame.maxY, height + 2, id + " " + language + " height " + String(Int(height)))
                }
                window.contentViewController = nil
            }
        }
    }

    func testPagesFitEveryWidthSchemeAndLanguage() throws {
        try requireWindowServer()
        let savedLanguage = TestLanguage.current()
        defer { TestLanguage.restore(savedLanguage) }
        let widths: [CGFloat] = [590, 760, 1000, 1280, 1920]
        var failures: [String] = []
        var scrollViews = 0
        var renders = 0
        for language in ["ru", "en"] {
            UserDefaults.standard.set(language, forKey: "UiLanguage")
            let model = try makeModel()
            let cases = try makeCases(for: model)
            for pageCase in cases {
                for width in widths {
                    for dark in [true, false] {
                        let outcome = render(pageCase.build(model), model: model, width: width, dark: dark, rtl: false)
                        renders += 1
                        if outcome.scrollViewFound { scrollViews += 1 }
                        for problem in outcome.problems {
                            let mode: String = dark ? "dark: " : "light: "
                            let widthText: String = String(Int(width))
                            failures.append("\(pageCase.name) \(language) \(widthText) \(mode)\(problem)")
                        }
                    }
                }
            }
        }
        XCTAssertEqual(renders, 120)
        if scrollViews == 0 { throw XCTSkip("SwiftUI scroll views are not visible to AppKit on this runner, so overflow cannot be measured.") }
        XCTAssertTrue(failures.isEmpty, failures.joined(separator: "\n"))
    }

    func testPagesFitInRightToLeftLayout() throws {
        try requireWindowServer()
        let savedLanguage = TestLanguage.current()
        defer { TestLanguage.restore(savedLanguage) }
        UserDefaults.standard.set("ru", forKey: "UiLanguage")
        let model = try makeModel()
        var failures: [String] = []
        for pageCase in try makeCases(for: model) {
            for width in [590, 1280] as [CGFloat] {
                let outcome = render(pageCase.build(model), model: model, width: width, dark: true, rtl: true)
                for problem in outcome.problems { failures.append(pageCase.name + " " + String(Int(width)) + " rtl: " + problem) }
            }
        }
        XCTAssertTrue(failures.isEmpty, failures.joined(separator: "\n"))
    }

    func testSheetsNeverExceedTheProposedWidth() throws {
        try requireWindowServer()
        let savedLanguage = TestLanguage.current()
        defer { TestLanguage.restore(savedLanguage) }
        let prompt: PairingPrompt = try TestFixtures.decode(PairingPrompt.self, "{\"id\":\"p\",\"name\":\"Рабочий компьютер\",\"code\":\"123456\"}")
        let model = try makeModel()
        let updater = MacUpdater()
        let sheets: [(String, AnyView)] = [
            ("folder", AnyView(ShellFolderSheet())),
            ("add-torrent", AnyView(AddTorrentSheet())),
            ("pairing", AnyView(ShellPairingSheet(prompt: prompt))),
            ("startup", AnyView(ShellStartupView()))
        ]
        var failures: [String] = []
        for language in ["ru", "en"] {
            UserDefaults.standard.set(language, forKey: "UiLanguage")
            for (name, sheet) in sheets {
                for width in [320, 590] as [CGFloat] {
                    let controller = NSHostingController(rootView: AnyView(sheet.environmentObject(model).environmentObject(updater)))
                    let size: CGSize = controller.sizeThatFits(in: CGSize(width: width, height: 10_000))
                    // The folder and add-torrent sheets are fixed-width dialogs; a narrower proposal may not shrink them.
                    let fixedWidth: Bool = name == "folder" || name == "add-torrent" || name == "pairing"
                    if size.width > width + 1 && !(fixedWidth && width < 460) {
                        let wantedText: String = String(Int(size.width))
                        let proposedText: String = String(Int(width))
                        failures.append("\(name) \(language) wants \(wantedText) pt for a proposal of \(proposedText) pt")
                    }
                }
            }
        }
        XCTAssertTrue(failures.isEmpty, failures.joined(separator: "\n"))
    }

    func testSettingsFollowTheColorScheme() throws {
        try requireWindowServer()
        let savedLanguage = TestLanguage.current()
        defer { TestLanguage.restore(savedLanguage) }
        UserDefaults.standard.set("ru", forKey: "UiLanguage")
        let model = try makeModel()
        func meanLuminance(dark: Bool) -> (mean: Double, deviation: Double) {
            let controller = NSHostingController(rootView: AnyView(SettingsView().environmentObject(model).environmentObject(MacUpdater()).preferredColorScheme(dark ? ColorScheme.dark : ColorScheme.light)))
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1000, height: 700), styleMask: [.borderless], backing: .buffered, defer: false)
            window.appearance = NSAppearance(named: dark ? NSAppearance.Name.darkAqua : NSAppearance.Name.aqua)
            window.contentViewController = controller
            window.setContentSize(NSSize(width: 1000, height: 700))
            controller.view.frame = NSRect(x: 0, y: 0, width: 1000, height: 700)
            controller.view.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.15))
            guard let bitmap = controller.view.bitmapImageRepForCachingDisplay(in: controller.view.bounds) else { return (0, 0) }
            controller.view.cacheDisplay(in: controller.view.bounds, to: bitmap)
            let stats = SmokePixelAudit.measure(bitmap, scale: window.backingScaleFactor, region: controller.view.bounds)
            window.contentViewController = nil
            return (stats.mean, stats.deviation)
        }
        let dark = meanLuminance(dark: true)
        let light = meanLuminance(dark: false)
        if dark.deviation < 0.005 && light.deviation < 0.005 { throw XCTSkip("Offscreen drawing produced flat images on this runner.") }
        XCTAssertGreaterThan(light.mean, dark.mean + 0.3, "light mean " + String(light.mean) + " dark mean " + String(dark.mean))
    }

    // MARK: Palette contrast

    private func rgb(_ color: NSColor, appearance name: NSAppearance.Name) -> (r: Double, g: Double, b: Double)? {
        guard let appearance = NSAppearance(named: name) else { return nil }
        var result: (r: Double, g: Double, b: Double)?
        appearance.performAsCurrentDrawingAppearance {
            if let converted = color.usingColorSpace(NSColorSpace.sRGB) {
                result = (Double(converted.redComponent), Double(converted.greenComponent), Double(converted.blueComponent))
            }
        }
        return result
    }

    private func luminance(_ value: (r: Double, g: Double, b: Double)) -> Double {
        func channel(_ component: Double) -> Double {
            return component <= 0.03928 ? component / 12.92 : pow((component + 0.055) / 1.055, 2.4)
        }
        return 0.2126 * channel(value.r) + 0.7152 * channel(value.g) + 0.0722 * channel(value.b)
    }

    private func contrast(_ a: (r: Double, g: Double, b: Double), _ b: (r: Double, g: Double, b: Double)) -> Double {
        let first: Double = luminance(a)
        let second: Double = luminance(b)
        return (max(first, second) + 0.05) / (min(first, second) + 0.05)
    }

    func testPaletteRolesAreLegibleInBothAppearances() throws {
        _ = NSApplication.shared
        let accentLight = rgb(NSColor(KDPalette.accent), appearance: NSAppearance.Name.aqua)
        let accentDark = rgb(NSColor(KDPalette.accent), appearance: NSAppearance.Name.darkAqua)
        guard let lightValue = accentLight, let darkValue = accentDark else { throw XCTSkip("The appearances could not be resolved.") }
        if abs(lightValue.r - darkValue.r) + abs(lightValue.g - darkValue.g) + abs(lightValue.b - darkValue.b) < 0.05 {
            throw XCTSkip("NSColor(Color) does not keep the dynamic appearance on this SDK, so the palette cannot be resolved per appearance.")
        }
        let roles: [(String, Color, Double)] = [
            ("accent", KDPalette.accent, 4.5), ("danger", KDPalette.danger, 4.5), ("warning", KDPalette.warning, 4.5),
            ("success", KDPalette.success, 4.5), ("info", KDPalette.info, 4.5)
        ]
        var failures: [String] = []
        for name in [NSAppearance.Name.aqua, NSAppearance.Name.darkAqua] {
            let label: String = name == NSAppearance.Name.aqua ? "light" : "dark"
            guard let page = rgb(NSColor.windowBackgroundColor, appearance: name), let surface = rgb(NSColor.controlBackgroundColor, appearance: name) else { continue }
            for (role, color, minimum) in roles {
                guard let foreground = rgb(NSColor(color), appearance: name) else { continue }
                for (backgroundName, background) in [("page", page), ("surface", surface)] {
                    let ratio: Double = contrast(foreground, background)
                    if ratio < minimum {
                        let ratioText: String = String(format: "%.2f", ratio)
                        let minimumText: String = String(minimum)
                        failures.append("\(role) on \(backgroundName) (\(label)) has contrast \(ratioText), needs \(minimumText)")
                    }
                }
            }
            if let fill = rgb(NSColor(KDPalette.accentFill), appearance: name), let ink = rgb(NSColor(KDPalette.accentInk), appearance: name) {
                let ratio: Double = contrast(fill, ink)
                if ratio < 4.5 {
                    let ratioText: String = String(format: "%.2f", ratio)
                    failures.append("accentInk on accentFill (\(label)) has contrast \(ratioText)")
                }
            }
        }
        XCTAssertTrue(failures.isEmpty, failures.joined(separator: "\n"))
    }

    // MARK: Source-level guards

    func testDesignKeepsTheAccessibilityEnvironment() throws {
        let design: String = try TestSources.text(of: "Design.swift")
        for key in ["accessibilityReduceTransparency", "colorSchemeContrast", "accessibilityReduceMotion"] {
            XCTAssertTrue(design.contains(key), "Design.swift no longer reads " + key)
        }
    }

    func testSmokeProbesExistInSources() throws {
        var probeLiterals = Set<String>()
        guard let expression = try? NSRegularExpression(pattern: "settingsLayoutProbe\\(\"([^\"]+)\"") else { return XCTFail("The probe pattern is invalid") }
        for file in try TestSources.swiftFiles() where file.name != "MacSmoke.swift" && file.name != "TestsSupport.swift" {
            let ns = file.text as NSString
            for match in expression.matches(in: file.text, range: NSRange(location: 0, length: ns.length)) {
                probeLiterals.insert(ns.substring(with: match.range(at: 1)))
            }
        }
        for literal in SmokeProbeCatalog.sourceLiterals {
            XCTAssertTrue(probeLiterals.contains(literal), "No view records the probe '" + literal + "', so the smoke test would silently stop measuring it")
        }
        for page in SmokePage.allCases {
            for id in page.spec.required {
                let covered: Bool = probeLiterals.contains(id) || probeLiterals.contains(where: { (literal: String) -> Bool in literal.hasSuffix(".") && id.hasPrefix(literal) })
                XCTAssertTrue(covered, page.rawValue + " requires the probe '" + id + "' that no view records")
            }
        }
    }

    func testSmokeIdentifiersExistInSources() throws {
        var all: String = ""
        for file in try TestSources.swiftFiles() where file.name != "MacSmoke.swift" && file.name != "TestsSupport.swift" {
            all += file.text + "\n"
        }
        for identifier in SmokeProbeCatalog.identifiers {
            let swiftUI = all.contains("accessibilityIdentifier(\"" + identifier)
            let appKit = all.contains("setAccessibilityIdentifier(\"" + identifier)
            XCTAssertTrue(swiftUI || appKit, "No view sets the accessibility identifier '" + identifier + "'")
        }
    }

    func testEverySettingsSectionHasACard() throws {
        let view: String = try TestSources.text(of: "SettingsView.swift")
        for section in SettingsSectionID.allCases {
            XCTAssertTrue(view.contains(".\(section.rawValue):"), "SettingsView has no card for " + section.rawValue)
        }
    }
}
