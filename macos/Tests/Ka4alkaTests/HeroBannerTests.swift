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
