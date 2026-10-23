import AppKit
import SwiftUI
import XCTest
@testable import Ka4alka

final class HeroRotationTests: XCTestCase {
    func testIdentitySurvivesRefreshAndWrapsInBothDirections() {
        XCTAssertEqual(HeroRotation.next(in: ["a", "b", "c"], selected: "c", delta: 1), "a")
        XCTAssertEqual(HeroRotation.next(in: ["a", "b", "c"], selected: "a", delta: -1), "c")
        XCTAssertEqual(HeroRotation.next(in: ["c", "a", "b"], selected: "b", delta: 1), "c")
        XCTAssertEqual(HeroRotation.next(in: ["a"], selected: "gone", delta: 1), "a")
        XCTAssertNil(HeroRotation.next(in: [], selected: "gone", delta: 1))
    }

    func testHeightAdaptsToShortWindowsWithoutChangingBetweenSlides() {
        XCTAssertEqual(CatalogHeroMetrics.height(forWidth: 1280), 560)
        XCTAssertEqual(CatalogHeroMetrics.height(forWidth: 1280, viewportHeight: 340), 300)
        XCTAssertEqual(CatalogHeroMetrics.height(forWidth: 460, viewportHeight: 300), 280)
    }

    func testNoClockRunsWhenItCannotBeUseful() {
        func allowed(count: Int = 5, visible: Bool = true, active: Bool = true,
                     interacting: Bool = false, paused: Bool = false, reduced: Bool = false) -> Bool {
            HeroRotation.canAdvance(count: count, visible: visible, active: active,
                                    interacting: interacting, paused: paused, reduced: reduced)
        }
        XCTAssertTrue(allowed())
        XCTAssertFalse(allowed(count: 1))
        XCTAssertFalse(allowed(visible: false))
        XCTAssertFalse(allowed(active: false))
        XCTAssertFalse(allowed(interacting: true))
        XCTAssertFalse(allowed(paused: true))
        XCTAssertFalse(allowed(reduced: true))
    }
}

@MainActor final class HeroBannerTests: XCTestCase {
    func testGameHeroReflowsWhenTheSameWindowShrinks() throws {
        _ = NSApplication.shared
        let previousProbe = SettingsLayoutMeasurements.enabledForTests
        SettingsLayoutMeasurements.enabledForTests = true
        defer { SettingsLayoutMeasurements.enabledForTests = previousProbe }
        let model = AppModel()
        model.bridge.disabled = true
        model.settings = try TestFixtures.decode(AppSettings.self, TestFixtures.settingsJSON)
        let card = try TestFixtures.card("games:resize", "Call of Duty: Modern Warfare", section: "games")
        let host = NSHostingView(rootView: DetailHero(card: card, detail: nil, onChooseRelease: {}).environmentObject(model))
        host.autoresizingMask = [.width, .height]
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1000, height: 420), styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = host
        for width in [1000, 496, 1000] as [CGFloat] {
            window.setContentSize(NSSize(width: width, height: 420))
            host.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.12))
            host.layoutSubtreeIfNeeded()
            let poster = try XCTUnwrap(SettingsLayoutMeasurements.frames["detail.hero.poster"])
            XCTAssertEqual(poster.width, width < 640 ? 100 : 135, accuracy: 1)
            XCTAssertEqual(poster.height / poster.width, 1.5, accuracy: 0.02)
            XCTAssertLessThanOrEqual(poster.maxX, width)
        }
        window.contentView = nil
    }

    func testFilmSeriesAndGameDetailPagesKeepOnlyArtworkFullWidth() throws {
        _ = NSApplication.shared
        let previousProbe = SettingsLayoutMeasurements.enabledForTests
        SettingsLayoutMeasurements.enabledForTests = true
        defer { SettingsLayoutMeasurements.enabledForTests = previousProbe }
        for section in ["movies", "series", "games"] {
            let model = AppModel()
            model.bridge.disabled = true
            model.settings = try TestFixtures.decode(AppSettings.self, TestFixtures.settingsJSON)
            let card = try TestFixtures.card(section + ":full-width", "A title that also wraps in a narrow window", section: section)
            let host = NSHostingView(rootView: DetailView(card: card).environmentObject(model))
            host.autoresizingMask = [.width, .height]
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1000, height: 700), styleMask: [.borderless], backing: .buffered, defer: false)
            window.contentView = host
            window.orderFront(nil)
            for width: CGFloat in [496, 1000, 1800, 496] {
                SettingsLayoutMeasurements.frames.removeAll()
                window.setContentSize(NSSize(width: width, height: 700))
                host.layoutSubtreeIfNeeded()
                RunLoop.main.run(until: Date().addingTimeInterval(0.15))
                host.layoutSubtreeIfNeeded()
                SettingsLayoutMeasurements.refreshNativeViews()
                let banner = try XCTUnwrap(SettingsLayoutMeasurements.frames["detail.banner"])
                let body = try XCTUnwrap(SettingsLayoutMeasurements.frames["detail.body"])
                let scroll = try XCTUnwrap(SettingsLayoutMeasurements.enclosingScrollView(for: "detail.banner"))
                XCTAssertEqual(banner.width, scroll.contentView.bounds.width, accuracy: 2, section)
                XCTAssertLessThanOrEqual(body.width, KD.Width.page + 1, section)
                XCTAssertLessThan(body.width, banner.width, section)
                XCTAssertGreaterThanOrEqual(body.minX - banner.minX, KD.gutter - 2, section)
            }
            window.orderOut(nil)
            window.contentView = nil
        }
    }

    func testDetailArtworkFillsVisibleSplitViewColumnWithSidebarShownOrHidden() throws {
        _ = NSApplication.shared
        let previousProbe = SettingsLayoutMeasurements.enabledForTests
        SettingsLayoutMeasurements.enabledForTests = true
        defer { SettingsLayoutMeasurements.enabledForTests = previousProbe }
        let model = AppModel()
        model.bridge.disabled = true
        model.settings = try TestFixtures.decode(AppSettings.self, TestFixtures.settingsJSON)
        model.connected = true
        model.route = .detail(try TestFixtures.card("movies:split-width", "A film"))
        let controller = NSHostingController(rootView: RootView().environmentObject(model).environmentObject(MacUpdater()))
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1280, height: 700), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        window.contentViewController = controller
        window.orderFront(nil)
        defer { window.orderOut(nil); window.contentViewController = nil }
        for visibility: NavigationSplitViewVisibility in [.all, .detailOnly] {
            model.sidebarVisibility = visibility
            for width: CGFloat in [760, 1280] {
                window.setContentSize(NSSize(width: width, height: 700))
                controller.view.layoutSubtreeIfNeeded()
                RunLoop.main.run(until: Date().addingTimeInterval(0.4))
                controller.view.layoutSubtreeIfNeeded()
                SettingsLayoutMeasurements.refreshNativeViews()
                let column = try XCTUnwrap(SettingsLayoutMeasurements.frames["shell.detail"])
                let clip = try XCTUnwrap(SettingsLayoutMeasurements.scrollViewportFrame(for: "detail.banner"))
                let banner = try XCTUnwrap(SettingsLayoutMeasurements.frames["detail.banner"])
                let visible = column.intersection(clip)
                XCTAssertFalse(visible.isNull)
                XCTAssertGreaterThan(visible.width, 300)
                XCTAssertEqual(banner.minX, visible.minX, accuracy: 2)
                XCTAssertEqual(banner.maxX, visible.maxX, accuracy: 2)
            }
        }
    }

    func testScrollVisibilityPublishesOnlyOnVisibilityChanges() {
        _ = NSApplication.shared
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 800, height: 400), styleMask: [.borderless], backing: .buffered, defer: false)
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 800, height: 400))
        let document = FlippedHeroDocument(frame: NSRect(x: 0, y: 0, width: 800, height: 1600))
        let probe = HeroVisibilityView(frame: NSRect(x: 0, y: 0, width: 800, height: 380))
        var events: [Bool] = []
        probe.changed = { events.append($0) }
        document.addSubview(probe)
        scroll.documentView = document
        window.contentView = scroll
        window.contentView?.layoutSubtreeIfNeeded()
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))
        XCTAssertEqual(events.last, true)
        scroll.contentView.scroll(to: NSPoint(x: 0, y: 600))
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))
        XCTAssertEqual(events.last, false)
        let count = events.count
        scroll.contentView.scroll(to: NSPoint(x: 0, y: 650))
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))
        XCTAssertEqual(events.count, count, "Normal scrolling must not invalidate the SwiftUI catalog")
        scroll.contentView.scroll(to: .zero)
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))
        XCTAssertEqual(events.last, true)
        probe.stop()
        window.contentView = nil
    }

    func testHeroControlsStayInsideBannerForLongTitlesInBothLanguages() throws {
        _ = NSApplication.shared
        let previousLanguage = TestLanguage.current()
        let previousProbe = SettingsLayoutMeasurements.enabledForTests
        defer {
            TestLanguage.restore(previousLanguage)
            SettingsLayoutMeasurements.enabledForTests = previousProbe
        }
        SettingsLayoutMeasurements.enabledForTests = true
        for language in ["ru", "en"] {
            UserDefaults.standard.set(language, forKey: "UiLanguage")
            for section in ["movies", "series", "games"] {
                let model = AppModel()
                model.bridge.disabled = true
                model.settings = try TestFixtures.decode(AppSettings.self, TestFixtures.settingsJSON)
                model.settings?.uiLanguage = language
                var card = try TestFixtures.card("hero:" + section, language == "ru" ? "Очень длинное название произведения для проверки узкого окна" : "A very long title to check the banner in a compact window", section: section)
                card.kp = "8.2"; card.imdb = "7.9"
                card.description = language == "ru" ? "Краткое описание, которое остаётся читаемым и не перекрывает кнопки даже в небольшом окне приложения." : "A short synopsis that stays readable without overlapping the buttons, including in a compact application window."
                for width in [460, 590, 1000, 1280] as [CGFloat] {
                    SettingsLayoutMeasurements.frames.removeAll()
                    let root = CatalogSpotlightView(items: [card, try TestFixtures.card("other", "Other")])
                        .environmentObject(model)
                        .frame(width: width)
                    let host = NSHostingView(rootView: root)
                    let height = CatalogHeroMetrics.height(forWidth: width)
                    let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: width, height: height), styleMask: [.borderless], backing: .buffered, defer: false)
                    window.contentView = host
                    host.frame = NSRect(x: 0, y: 0, width: width, height: height)
                    host.layoutSubtreeIfNeeded()
                    RunLoop.main.run(until: Date().addingTimeInterval(0.08))
                    host.layoutSubtreeIfNeeded()
                    let failures = SettingsLayoutMeasurements.audit(prefix: "catalog.hero", windowWidth: width)
                    XCTAssertTrue(failures.isEmpty, "\(language)/\(section)/\(width): " + failures.joined(separator: "; "))
                    XCTAssertNotNil(SettingsLayoutMeasurements.frames["catalog.hero.details"])
                    XCTAssertNotNil(SettingsLayoutMeasurements.frames["catalog.hero.save"])
                    window.contentView = nil
                }
            }
        }
    }
}

private final class FlippedHeroDocument: NSView {
    override var isFlipped: Bool { true }
}
