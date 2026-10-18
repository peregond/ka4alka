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
            if let first = model.list?.items.first {
                let before = model.isSaved(first); await model.save(first)
                try check(model.isSaved(first) != before, "Saved titles use the shared preference engine")
                await model.save(first)
                model.open(first); try await shot("detail-scaffold-ru"); model.back()
                try check(model.route == .section(.movies), "Back preserves the catalog route")
            }
            stage("series"); model.select(.series); await model.loadCatalog(); try check(model.list?.items.isEmpty == false, "Series catalog is available through the same engine")
            try await shot("series-ru")
            stage("games"); model.select(.games); model.filter.genre = "all"; await model.loadCatalog()
            try check(model.list?.items.isEmpty == false, "The Windows game catalog uses real Steam entries on macOS")
            try await shot("games-ru")
            model.select(.games); try await Task.sleep(for: .seconds(3)); try await shot("game-home-ru")
            model.select(.downloads); try await shot("downloads-ru")
            model.select(.settings); try await shot("settings-ru")
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
