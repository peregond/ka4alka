import Foundation

enum HeroRotation {
    static let interval = 8
    struct Clock: Equatable {
        var ids: [String]
        var selected: String?
        var running: Bool
    }

    static func canAdvance(count: Int, visible: Bool, active: Bool, interacting: Bool, paused: Bool, reduced: Bool) -> Bool {
        count > 1 && visible && active && !interacting && !paused && !reduced
    }

    /// Resolves against identity rather than an index, so refresh/hide cannot select
    /// a different item accidentally. Removing the selection falls back to the first.
    static func next(in ids: [String], selected: String?, delta: Int) -> String? {
        guard !ids.isEmpty else { return nil }
        let current = ids.firstIndex { $0 == selected } ?? 0
        return ids[((current + delta) % ids.count + ids.count) % ids.count]
    }
}
