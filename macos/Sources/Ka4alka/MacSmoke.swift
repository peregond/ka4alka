import AppKit
import Foundation

@MainActor enum MacSmoke {
    static func close(model: AppModel) async {
        guard let index = CommandLine.arguments.firstIndex(of: "--close-smoke-test"), CommandLine.arguments.count > index + 1 else { return }
        let directory = URL(fileURLWithPath: CommandLine.arguments[index + 1])
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            try await Task.sleep(for: .milliseconds(300))
            guard model.connected, let window = NSApp.windows.first(where: { $0.isVisible && $0.contentView != nil }) else { throw UpdateError("Native close test has no connected window") }
            try JSONEncoder().encode(["connected": true, "nativeWindowClose": true]).write(to: directory.appendingPathComponent("checks.json"))
            window.performClose(nil)
        } catch {
            try? error.localizedDescription.write(to: directory.appendingPathComponent("error.txt"), atomically: true, encoding: .utf8)
            requestApplicationQuit()
        }
    }
    static func run(model: AppModel) async {
        guard let index = CommandLine.arguments.firstIndex(of: "--smoke-test"), CommandLine.arguments.count > index + 1 else { return }
        let root = URL(fileURLWithPath: CommandLine.arguments[index + 1]); try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        var checks: [String] = []
        func stage(_ name: String) { try? name.write(to: root.appendingPathComponent("stage.txt"), atomically: true, encoding: .utf8) }
        func check(_ value: Bool, _ text: String) throws { if !value { throw UpdateError(text) }; checks.append(text) }
        func shot(_ name: String) async throws {
            try await Task.sleep(for: .milliseconds(800))
            guard let window = NSApp.windows.first(where: { $0.contentView != nil && $0.isVisible }), let content = window.contentView else { throw UpdateError("The native application window is missing") }
            let view = content.superview ?? content
            window.makeKeyAndOrderFront(nil); view.layoutSubtreeIfNeeded()
            guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw UpdateError("Could not capture the native content view") }
            view.cacheDisplay(in: view.bounds, to: bitmap)
            guard let png = bitmap.representation(using: .png, properties: [:]) else { throw UpdateError("Could not encode the native screenshot") }
            try png.write(to: root.appendingPathComponent(name + ".png"))
        }
        func settingsLayout(_ name: String, bottom: Bool) throws {
            let frames = SettingsLayoutMeasurements.frames
            let report = frames.mapValues { frame in
                ["x": Double(frame.minX), "y": Double(frame.minY), "width": Double(frame.width), "height": Double(frame.height)]
            }
            try JSONEncoder().encode(report).write(to: root.appendingPathComponent(name + "-layout.json"))
            guard let viewport = frames["viewport"] else { throw UpdateError("Settings viewport was not measured") }
            let ids = ["appearance", "downloads", "updates", "ignored", "lan", "sources", "cache", "metadata", "help"]
            let cards = try ids.map { id -> CGRect in
                guard let frame = frames["card." + id] else { throw UpdateError("Settings card was not measured: " + id) }
                return frame
            }
            try check(cards.allSatisfy { $0.width > 280 && $0.height > 30 && $0.minX >= viewport.minX - 2 && $0.maxX <= viewport.maxX + 2 }, name + ": all settings cards fit horizontally")
            try check(cards.allSatisfy { abs($0.minX - cards[0].minX) < 2 && abs($0.width - cards[0].width) < 2 }, name + ": settings cards share one leading edge and width")
            try check(zip(cards, cards.dropFirst()).allSatisfy { first, next in next.minY >= first.maxY - 2 && next.minY - first.maxY < 80 }, name + ": settings cards form one continuous vertical column")
            if bottom {
                let help = cards[cards.count - 1]
                try check(help.maxY <= viewport.maxY + 3 && help.intersection(viewport).height >= min(help.height, viewport.height) * 0.8, name + ": final settings section is reachable by scrolling")
            } else {
                let appearance = cards[0]
                try check(appearance.minY >= viewport.minY - 2 && appearance.minY < viewport.maxY && appearance.intersection(viewport).height > 30, name + ": first settings section is visible below the toolbar")
            }
        }
        func scrollSettings(toBottom: Bool) async throws {
            guard let content = NSApp.windows.first(where: { $0.contentView != nil && $0.isVisible })?.contentView else { throw UpdateError("Settings window is missing") }
            func scrollViews(_ view: NSView) -> [NSScrollView] {
                (view as? NSScrollView).map { [$0] } ?? view.subviews.flatMap(scrollViews)
            }
            guard let scroll = scrollViews(content).filter({ $0.bounds.width > 300 && ($0.documentView?.bounds.height ?? 0) > $0.contentView.bounds.height + 10 }).max(by: { ($0.documentView?.bounds.height ?? 0) < ($1.documentView?.bounds.height ?? 0) }), let document = scroll.documentView else { throw UpdateError("The settings page must scroll to expose all sections") }
            let low = document.bounds.minY
            let high = max(low, document.bounds.maxY - scroll.contentView.bounds.height)
            let y = toBottom == document.isFlipped ? high : low
            scroll.contentView.scroll(to: NSPoint(x: document.bounds.minX, y: y))
            scroll.reflectScrolledClipView(scroll.contentView)
            try await Task.sleep(for: .milliseconds(150))
        }
        func settingsScenarios() async throws {
            stage("settings-layout")
            model.select(.settings)
            for language in ["ru", "en"] {
                await model.set("uiLanguage", .string(language))
                for light in [false, true] {
                    await model.set("light", .bool(light))
                    for compact in [false, true] {
                        guard let window = NSApp.windows.first(where: { $0.isVisible }) else { throw UpdateError("The settings window is missing") }
                        window.setContentSize(NSSize(width: compact ? 800 : 1180, height: compact ? 600 : 760))
                        try await Task.sleep(for: .milliseconds(300))
                        try await scrollSettings(toBottom: false)
                        let name = "settings-" + language + (light ? "-light" : "-dark") + (compact ? "-compact" : "-wide")
                        try await shot(name)
                        try settingsLayout(name, bottom: false)
                        try await scrollSettings(toBottom: true)
                        try await shot(name + "-bottom")
                        try settingsLayout(name + "-bottom", bottom: true)
                    }
                }
            }
            await model.set("uiLanguage", .string("ru")); await model.set("light", .bool(false))
            NSApp.windows.first(where: { $0.isVisible })?.setContentSize(NSSize(width: 1180, height: 760))
        }
        func catalogBanner(_ section: String) throws {
            guard let banner = SettingsLayoutMeasurements.frames["catalog.banner." + section], let window = NSApp.windows.first(where: { $0.isVisible }), let content = window.contentView else { throw UpdateError("The " + section + " catalog is missing its spotlight banner") }
            try check(banner.width > 280 && banner.height >= 280 && banner.height < 420, section + ": spotlight banner has a complete native layout")
            try check(banner.minX >= -2 && banner.maxX <= content.bounds.width + 2 && banner.minY >= -2 && banner.maxY <= content.bounds.height + 2, section + ": spotlight banner is visible at the top of the catalog")
            try JSONEncoder().encode(["x": Double(banner.minX), "y": Double(banner.minY), "width": Double(banner.width), "height": Double(banner.height)]).write(to: root.appendingPathComponent(section + "-banner-layout.json"))
        }
        do {
            stage("bootstrap")
            try check(model.connected, "Native SwiftUI application launches its packaged shared engine")
            model.folderRequired = false
            stage("featured"); model.select(.featured); await model.loadFeatured()
            try check(!model.curated.isEmpty, "Featured resolves real bundled catalog membership")
            try await shot("featured-ru")
            stage("movies"); model.select(.movies); await model.loadCatalog()
            try await Task.sleep(for: .seconds(2))
            try check(model.list?.items.isEmpty == false, "Movie catalog loads from the shared snapshot")
            try await shot("movies-ru")
            try catalogBanner("movies")
            if let first = model.list?.items.first {
                let before = model.isSaved(first); await model.save(first)
                try check(model.isSaved(first) != before, "Saved titles use the shared preference engine")
                await model.save(first)
                model.open(first); try await shot("detail-scaffold-ru"); model.back()
                try check(model.route == .section(.movies), "Back preserves the catalog route")
            }
            stage("series"); model.select(.series); await model.loadCatalog(); try check(model.list?.items.isEmpty == false, "Series catalog is available through the same engine")
            try await shot("series-ru")
            try catalogBanner("series")
            stage("games"); model.select(.games); model.filter.genre = "all"; await model.loadCatalog()
            try check(model.list?.items.isEmpty == false, "The Windows game catalog uses real Steam entries on macOS")
            try await shot("games-ru")
            model.select(.games); try await Task.sleep(for: .seconds(3)); try await shot("game-home-ru")
            model.select(.downloads); try await shot("downloads-ru")
            try await settingsScenarios()
            stage("english"); await model.set("uiLanguage", .string("en")); model.select(.featured); await model.loadFeatured(); try await shot("featured-en")
            try check(model.curated.first?.title.range(of: #"[А-Яа-я]"#, options: .regularExpression) == nil, "Featured labels use authored English translations")
            if let window = NSApp.windows.first(where: { $0.isVisible }) { window.setContentSize(NSSize(width: 800, height: 600)); try await shot("compact-en") }
            await model.set("light", .bool(true)); try await shot("light-en")
            await model.set("uiLanguage", .string("ru")); await model.set("light", .bool(false))
            let artwork = JSONValue.object(["loaded": .number(Double(ImageStore.shared.loadedCount)), "failed": .number(Double(ImageStore.shared.failedCount)), "errors": .array(await ArtworkDiagnostics.shared.snapshot().map(JSONValue.string))])
            try JSONEncoder().encode(artwork).write(to: root.appendingPathComponent("artwork.json"))
            let data = try JSONEncoder().encode(checks); try data.write(to: root.appendingPathComponent("checks.json"))
        } catch { try? error.localizedDescription.write(to: root.appendingPathComponent("error.txt"), atomically: true, encoding: .utf8) }
        stage("shutdown"); requestApplicationQuit()
    }
}
