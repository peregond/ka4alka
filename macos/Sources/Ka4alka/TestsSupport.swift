import AppKit
import MachO
import Foundation

// Helpers for the packaged smoke test (MacSmoke.swift) and the layout unit tests.
// Nothing here runs during a normal launch.

// MARK: Report types

/// One layout, accessibility, pixel or performance problem. The workflow compares these with the checked-in allowlist.
struct SmokeViolation: Codable {
    var category: String
    var page: String
    var width: Int
    var height: Int
    var theme: String
    var language: String
    var text: String
}

/// One cell of the page x size x theme x language matrix.
struct SmokeCell: Codable {
    var page: String
    var requested: String
    var width: Int
    var height: Int
    var theme: String
    var language: String
    var violations: Int
    var meanLuminance: Double
    var deviation: Double
}

struct SmokeReport: Codable {
    var checks: [String]
    var failures: [String]
    var notes: [String]
    var warnings: [String]
    var metrics: [String: Double]
    var environment: [String: String]
}

// MARK: Pages of the matrix

enum SmokePage: String, CaseIterable {
    case featured
    case search
    case movies
    case series
    case gamesHome = "games-home"
    case gamesGrid = "games-grid"
    case saved
    case downloadsEmpty = "downloads-empty"
    case downloadsQueue = "downloads-queue"
    case settings
    case detailMovie = "detail-movie"
    case detailSeries = "detail-series"
    case detailGame = "detail-game"
    case person
    case award
    case folderSheet = "folder-sheet"
    case addSheet = "add-sheet"
}

struct SmokePageSpec {
    /// Probe id prefixes passed to `SettingsLayoutMeasurements.audit`.
    var prefixes: [String]
    /// Probe ids that must exist on the page, otherwise the page lost its instrumentation.
    var required: [String]
    /// The page needs the network (Steam, trackers, TMDB): a load error is a warning, not a failure.
    var network: Bool
    /// Mostly flat background: the mean luminance must match the colour scheme.
    var plain: Bool
    /// Run the native toolbar checks on this page.
    var toolbar: Bool
    /// Scan the accessibility texts for the wrong language.
    var scansText: Bool
}

extension SmokePage {
    var spec: SmokePageSpec {
        switch self {
        case .search:
            return SmokePageSpec(prefixes: ["search."], required: ["search.content", "search.field"], network: false, plain: true, toolbar: false, scansText: true)
        case .featured:
            return SmokePageSpec(prefixes: ["featured."], required: ["featured.hero"], network: false, plain: false, toolbar: false, scansText: false)
        case .movies:
            return SmokePageSpec(prefixes: ["catalog."], required: ["catalog.banner.movies", "catalog.hero"], network: false, plain: false, toolbar: true, scansText: false)
        case .series:
            return SmokePageSpec(prefixes: ["catalog."], required: ["catalog.banner.series", "catalog.hero"], network: false, plain: false, toolbar: false, scansText: false)
        case .gamesHome:
            return SmokePageSpec(prefixes: ["games."], required: ["games.hero"], network: true, plain: false, toolbar: false, scansText: false)
        case .gamesGrid:
            return SmokePageSpec(prefixes: ["catalog."], required: [], network: true, plain: false, toolbar: false, scansText: false)
        case .saved:
            return SmokePageSpec(prefixes: ["catalog."], required: ["catalog.saved.controls"], network: false, plain: false, toolbar: false, scansText: false)
        case .downloadsEmpty:
            return SmokePageSpec(prefixes: ["downloads."], required: ["downloads.header"], network: false, plain: true, toolbar: false, scansText: true)
        case .downloadsQueue:
            return SmokePageSpec(prefixes: ["downloads."], required: ["downloads.header", "downloads.tiles", "downloads.row.0"], network: false, plain: false, toolbar: false, scansText: false)
        case .settings:
            let cards: [String] = SettingsSectionID.allCases.map { (section: SettingsSectionID) -> String in "card." + section.rawValue }
            return SmokePageSpec(prefixes: ["card.", "settings.", "appearance.", "header", "content", "viewport"], required: ["header", "viewport", "content"] + cards, network: false, plain: true, toolbar: false, scansText: true)
        case .detailMovie, .detailSeries, .detailGame:
            return SmokePageSpec(prefixes: ["detail."], required: ["detail.banner"], network: true, plain: false, toolbar: false, scansText: false)
        case .person, .award:
            return SmokePageSpec(prefixes: [], required: [], network: true, plain: false, toolbar: false, scansText: false)
        case .folderSheet, .addSheet:
            return SmokePageSpec(prefixes: [], required: [], network: false, plain: false, toolbar: false, scansText: true)
        }
    }
}

/// The probe ids the smoke test relies on and the literal that must appear in Sources for each.
/// A unit test greps the sources for every literal, so a renamed probe fails fast instead of silently weakening the smoke test.
enum SmokeProbeCatalog {
    static let sourceLiterals: [String] = [
        "featured.hero", "featured.digest", "featured.ranking",
        "search.content", "search.field",
        "catalog.banner.", "catalog.hero", "catalog.hero.title", "catalog.hero.actions", "catalog.filters", "catalog.saved.controls",
        "games.hero",
        "downloads.header", "downloads.tiles", "downloads.row.",
        "viewport", "header", "content", "card.", "settings.nav", "appearance.tile.",
        "detail.banner", "detail.actions", "detail.releases.toolbar",
        "toolbar.sidebar", "toolbar.add", "toolbar.refresh", "toolbar.appearance", "sidebar"
    ]
    /// Accessibility identifiers the smoke test or the UI tests may look for.
    static let identifiers: [String] = ["nav-", "nav-search", "shell-sidebar-toggle", "search-field", "catalog-spotlight", "hero-open", "downloads-add"]
}

// MARK: Text rules

enum SmokeText {
    private static let brandWords: Set<String> = [
        "ka4alka", "steam", "imdb", "tmdb", "thetvdb", "kinopoisk", "github", "windows", "macos", "mac", "apple", "torrent",
        "bittorrent", "telegram", "discord", "api", "lite", "lan", "wi", "fi", "the", "tv", "db", "gog", "epic", "games", "metacritic"
    ]

    static func hasCyrillic(_ text: String) -> Bool {
        return text.range(of: "[А-Яа-яЁё]", options: .regularExpression) != nil
    }

    /// Latin phrases of two or more words that are not just brand names.
    static func latinPhrases(in text: String) -> [String] {
        guard let expression = try? NSRegularExpression(pattern: "[A-Za-z]{3,}(?:\\s+[A-Za-z]{2,})+") else { return [] }
        let whole = NSRange(text.startIndex..<text.endIndex, in: text)
        var phrases: [String] = []
        for match in expression.matches(in: text, range: whole) {
            guard let range = Range(match.range, in: text) else { continue }
            let phrase: String = String(text[range])
            let words: [String] = phrase.split(separator: " ").map { (word: Substring) -> String in String(word).lowercased() }
            let onlyBrands: Bool = words.allSatisfy { (word: String) -> Bool in brandWords.contains(word) }
            if !onlyBrands { phrases.append(phrase) }
        }
        return phrases
    }

    /// An SF Symbol name read out by VoiceOver instead of a sentence.
    static func looksLikeSymbolName(_ text: String) -> Bool {
        if text.range(of: "^[a-z][a-z0-9]*(\\.[a-z0-9]+)+$", options: .regularExpression) != nil { return true }
        let bare: [String] = ["plus", "heart", "film", "tv", "gearshape", "star", "xmark", "trash", "folder", "sparkles", "gamecontroller"]
        return bare.contains(text)
    }
}

// MARK: Views

enum SmokeViews {
    static func all(in root: NSView) -> [NSView] {
        var result: [NSView] = [root]
        for child in root.subviews { result.append(contentsOf: all(in: child)) }
        return result
    }

    /// The window's toolbar lives in the theme frame, one level above the content view.
    static func searchField(in window: NSWindow) -> NSSearchField? {
        guard let content = window.contentView else { return nil }
        let root: NSView = content.superview ?? content
        let fields: [NSSearchField] = all(in: root).compactMap { (view: NSView) -> NSSearchField? in view as? NSSearchField }
        return fields.first { (field: NSSearchField) -> Bool in !field.isHiddenOrHasHiddenAncestor } ?? fields.first
    }

    static func largestScrollView(in window: NSWindow) -> NSScrollView? {
        guard let content = window.contentView else { return nil }
        let scrolls: [NSScrollView] = all(in: content).compactMap { (view: NSView) -> NSScrollView? in view as? NSScrollView }
        return scrolls.max { (first: NSScrollView, second: NSScrollView) -> Bool in
            let a: CGFloat = first.contentView.bounds.width * first.contentView.bounds.height
            let b: CGFloat = second.contentView.bounds.width * second.contentView.bounds.height
            return a < b
        }
    }

    /// Rectangle of a SwiftUI `.global` frame in screen coordinates.
    static func screenRect(forGlobal frame: CGRect, in window: NSWindow) -> CGRect {
        guard let content = window.contentView else { return .zero }
        let viewRect: CGRect
        if content.isFlipped {
            viewRect = frame
        } else {
            viewRect = CGRect(x: frame.minX, y: content.bounds.height - frame.maxY, width: frame.width, height: frame.height)
        }
        let inWindow: CGRect = content.convert(viewRect, to: nil)
        return window.convertToScreen(inWindow)
    }
}

// MARK: Accessibility tree

struct SmokeAccessibilityNode {
    var role: String
    var label: String
    var frame: CGRect
}

/// Reads the in-process accessibility tree through selectors, so it works for SwiftUI's private element classes too.
enum SmokeAccessibility {
    static let interactiveRoles: Set<String> = [
        NSAccessibility.Role.button.rawValue, NSAccessibility.Role.checkBox.rawValue, NSAccessibility.Role.popUpButton.rawValue,
        NSAccessibility.Role.link.rawValue, NSAccessibility.Role.menuButton.rawValue, NSAccessibility.Role.radioButton.rawValue
    ]
    static let textRoles: Set<String> = [NSAccessibility.Role.staticText.rawValue]

    static func nodes(in window: NSWindow) -> [SmokeAccessibilityNode] {
        var result: [SmokeAccessibilityNode] = []
        guard let content = window.contentView else { return result }
        visit(content, depth: 0, result: &result)
        return result
    }

    private static func value(_ target: NSObject, _ name: String) -> Any? {
        let selector: Selector = Selector((name))
        guard target.responds(to: selector) else { return nil }
        return target.value(forKey: name)
    }

    private static func visit(_ element: Any, depth: Int, result: inout [SmokeAccessibilityNode]) {
        if depth > 40 || result.count > 5000 { return }
        guard let node = element as? NSObject else { return }
        let role: String = (value(node, "accessibilityRole") as? String) ?? ""
        var label: String = (value(node, "accessibilityLabel") as? String) ?? ""
        if label.isEmpty { label = (value(node, "accessibilityTitle") as? String) ?? "" }
        if label.isEmpty { label = (value(node, "accessibilityValue") as? String) ?? "" }
        var frame: CGRect = .zero
        if let boxed = value(node, "accessibilityFrame") as? NSValue { frame = boxed.rectValue }
        result.append(SmokeAccessibilityNode(role: role, label: label, frame: frame))
        let children: [Any] = (value(node, "accessibilityChildren") as? [Any]) ?? []
        for child in children { visit(child, depth: depth + 1, result: &result) }
    }
}

// MARK: Pixels

struct SmokePixelStats {
    var mean: Double = 0
    var deviation: Double = 0
    var darkShare: Double = 0
    var lightShare: Double = 0
    var signature: UInt64 = 0
    var samples: Int = 0
}

enum SmokePixelAudit {
    /// Samples a grid of about 160 x 100 points of `region` (in points, origin top left).
    static func measure(_ bitmap: NSBitmapImageRep, scale: CGFloat, region: CGRect) -> SmokePixelStats {
        var stats = SmokePixelStats()
        let x0: Int = max(0, Int((region.minX * scale).rounded(.down)))
        let y0: Int = max(0, Int((region.minY * scale).rounded(.down)))
        let x1: Int = min(bitmap.pixelsWide, Int((region.maxX * scale).rounded(.up)))
        let y1: Int = min(bitmap.pixelsHigh, Int((region.maxY * scale).rounded(.up)))
        guard x1 > x0, y1 > y0 else { return stats }
        let stepX: Int = max(1, (x1 - x0) / 160)
        let stepY: Int = max(1, (y1 - y0) / 100)
        var sum: Double = 0
        var sumSquares: Double = 0
        var dark: Int = 0
        var light: Int = 0
        var count: Int = 0
        var hash: UInt64 = 1_469_598_103_934_665_603
        var y: Int = y0
        while y < y1 {
            var x: Int = x0
            while x < x1 {
                if let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(NSColorSpace.sRGB) {
                    let value: Double = 0.2126 * Double(color.redComponent) + 0.7152 * Double(color.greenComponent) + 0.0722 * Double(color.blueComponent)
                    sum += value
                    sumSquares += value * value
                    if value < 0.35 { dark += 1 }
                    if value > 0.65 { light += 1 }
                    count += 1
                    let level: UInt64 = UInt64(min(255, max(0, Int(value * 255))))
                    hash = (hash ^ level) &* 1_099_511_628_211
                }
                x += stepX
            }
            y += stepY
        }
        guard count > 0 else { return stats }
        let mean: Double = sum / Double(count)
        let variance: Double = max(0, sumSquares / Double(count) - mean * mean)
        stats.mean = mean
        stats.deviation = variance.squareRoot()
        stats.darkShare = Double(dark) / Double(count)
        stats.lightShare = Double(light) / Double(count)
        stats.signature = hash
        stats.samples = count
        return stats
    }
}

// MARK: Main thread responsiveness

/// Measures the gaps between 8 ms timer ticks on the main run loop. A long gap is a frame the person would have seen stall.
final class SmokeStallMonitor: @unchecked Sendable {
    private let lock = NSLock()
    private var timer: Timer?
    private var last: CFAbsoluteTime = 0
    private var gaps: [Double] = []

    func start() {
        lock.lock()
        last = CFAbsoluteTimeGetCurrent()
        gaps = []
        lock.unlock()
        let timer = Timer(timeInterval: 0.008, repeats: true) { [weak self] (_: Timer) in self?.tick() }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func tick() {
        let now: CFAbsoluteTime = CFAbsoluteTimeGetCurrent()
        lock.lock()
        let gap: Double = (now - last) * 1000
        last = now
        if gap > 40 { gaps.append(gap) }
        lock.unlock()
    }

    /// Longest gap in milliseconds and the number of gaps over 100 ms.
    func stop() -> (maxGap: Double, over100: Int) {
        timer?.invalidate()
        timer = nil
        lock.lock()
        defer { lock.unlock() }
        let longest: Double = gaps.max() ?? 0
        let slow: Int = gaps.filter { (gap: Double) -> Bool in gap > 100 }.count
        return (longest, slow)
    }
}

// MARK: Process facts

func smokeResidentMegabytes() -> Double {
    var info = mach_task_basic_info()
    var count = mach_msg_type_number_t(MemoryLayout<mach_task_basic_info>.size / MemoryLayout<natural_t>.size)
    let result: kern_return_t = withUnsafeMutablePointer(to: &info) { (pointer: UnsafeMutablePointer<mach_task_basic_info>) -> kern_return_t in
        pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { (reboundPointer: UnsafeMutablePointer<integer_t>) -> kern_return_t in
            task_info(mach_task_self_, task_flavor_t(MACH_TASK_BASIC_INFO), reboundPointer, &count)
        }
    }
    guard result == KERN_SUCCESS else { return 0 }
    return Double(info.resident_size) / 1_048_576
}

func smokeProcessStart() -> Date? {
    var info = kinfo_proc()
    var size: Int = MemoryLayout<kinfo_proc>.stride
    var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid()]
    let result: Int32 = sysctl(&mib, UInt32(mib.count), &info, &size, nil, 0)
    guard result == 0 else { return nil }
    let started = info.kp_proc.p_un.__p_starttime
    return Date(timeIntervalSince1970: TimeInterval(started.tv_sec) + TimeInterval(started.tv_usec) / 1_000_000)
}

/// Which surface variant the UI is using right now: macOS 26 glass, material on 14/15, or opaque under Reduce Transparency.
@MainActor func smokeSurfaceVariant() -> String {
    if NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency { return "solid" }
    #if compiler(>=6.2)
    if #available(macOS 26.0, *) { return "glass" }
    #endif
    return "material"
}

/// The SDK version the running executable was linked against, from its LC_BUILD_VERSION command, as (major, minor).
/// macOS draws the Liquid Glass sidebar and toolbar only for an executable linked against the macOS 26 SDK or later.
func smokeLinkedSDK() -> (major: Int, minor: Int)? {
    guard let header = _dyld_get_image_header(0) else { return nil }
    let base = UnsafeRawPointer(header)
    let count = Int(header.pointee.ncmds)
    var offset = MemoryLayout<mach_header_64>.size
    for _ in 0..<count {
        let command = base.load(fromByteOffset: offset, as: load_command.self)
        if command.cmd == UInt32(LC_BUILD_VERSION) {
            let build = base.load(fromByteOffset: offset, as: build_version_command.self)
            return (Int(build.sdk >> 16), Int((build.sdk >> 8) & 0xFF))
        }
        offset += Int(command.cmdsize)
    }
    return nil
}

func smokeGlassBranchCompiled() -> Bool {
    #if compiler(>=6.2)
    return true
    #else
    return false
    #endif
}

// MARK: Fixtures

enum SmokeFixtures {
    /// Five queue entries that stress the row layout: active, paused, missing files, a long series title with "update release", and unknown size.
    static func transfers() -> [Transfer] {
        let longTitle: String = String(String(repeating: "Extremely long series title for the queue ", count: 4).prefix(90))
        let active = Transfer(id: "fixture-1", title: "Fixture Movie", releaseTitle: "Fixture.Movie.2026.1080p.WEB-DL", poster: nil, progress: 42.5, status: "Downloading", hint: "", paused: false, complete: false, missing: false, lowSpace: false, downloadRate: 3_400_000, uploadRate: 120_000, totalBytes: 4_200_000_000, addedUtc: "2026-10-01T10:00:00Z", folder: "/tmp/ka4alka-fixture/1", mediaId: nil, canUpdateRelease: false, peers: "12 peers", files: 3, statusKind: "downloading", remaining: "12 min left", indeterminate: false)
        let paused = Transfer(id: "fixture-2", title: "Paused Fixture", releaseTitle: "Paused.Fixture.2025.720p", poster: nil, progress: 10, status: "Paused", hint: "", paused: true, complete: false, missing: false, lowSpace: false, downloadRate: 0, uploadRate: 0, totalBytes: 1_500_000_000, addedUtc: "2026-10-01T09:00:00Z", folder: "/tmp/ka4alka-fixture/2", mediaId: nil, canUpdateRelease: false, peers: "", files: 1, statusKind: "paused", remaining: "", indeterminate: false)
        let missing = Transfer(id: "fixture-3", title: "Missing Files Fixture", releaseTitle: "Missing.Files.2024.2160p", poster: nil, progress: 100, status: "Files not found", hint: "The files were deleted from the folder.", paused: false, complete: true, missing: true, lowSpace: false, downloadRate: 0, uploadRate: 0, totalBytes: 9_000_000_000, addedUtc: "2026-09-30T09:00:00Z", folder: "/tmp/ka4alka-fixture/3", mediaId: nil, canUpdateRelease: false, peers: "", files: 2, statusKind: "error", remaining: "", indeterminate: false)
        let series = Transfer(id: "fixture-4", title: longTitle, releaseTitle: "Fixture.Series.S02.1080p.WEB-DL", poster: nil, progress: 77.7, status: "Seeding", hint: "", paused: false, complete: false, missing: false, lowSpace: false, downloadRate: 800_000, uploadRate: 2_500_000, totalBytes: 18_000_000_000, addedUtc: "2026-09-29T09:00:00Z", folder: "/tmp/ka4alka-fixture/4", mediaId: "series:series:fixture", canUpdateRelease: true, peers: "40 peers", files: 12, statusKind: "downloading", remaining: "3 h left", indeterminate: false)
        let unknown = Transfer(id: "fixture-5", title: "Unknown Size Fixture", releaseTitle: "Unknown.Size.2026", poster: nil, progress: 0, status: "Checking", hint: "", paused: false, complete: false, missing: false, lowSpace: false, downloadRate: 0, uploadRate: 0, totalBytes: nil, addedUtc: "2026-09-28T09:00:00Z", folder: "/tmp/ka4alka-fixture/5", mediaId: nil, canUpdateRelease: false, peers: "", files: 0, statusKind: "checking", remaining: "", indeterminate: true)
        return [active, paused, missing, series, unknown]
    }
}
