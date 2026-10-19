import AppKit
import SwiftUI

// Capture real SwiftUI layout in the packaged native smoke test. Normal app
// launches do not install geometry readers or retain this diagnostic state.

/// What a probed view is, so the audit can apply the right rule to it.
enum LayoutProbeKind {
    /// A region that holds other probes (a card, a column, a toolbar strip).
    case container
    /// Something the pointer or keyboard operates: it must be large enough and must not overlap its siblings.
    case control
    /// A run of text: it must stay inside its group.
    case text
}

/// Read once; the launch arguments never change while the app runs.
private let layoutProbeEnabled: Bool = CommandLine.arguments.contains("--smoke-test")

@MainActor enum SettingsLayoutMeasurements {
    static var isSmoke: Bool { layoutProbeEnabled }
    static var frames: [String: CGRect] = [:]
    static var kinds: [String: LayoutProbeKind] = [:]
    static var groups: [String: String] = [:]
    static var nativeFrames: [String: CGRect] = [:]
    static var nativeVisibleFrames: [String: CGRect] = [:]
    static var nativeAttached: [String: Bool] = [:]
    private static var owners: [String: UUID] = [:]
    private final class WeakView {
        weak var view: NSView?
        init(_ view: NSView) { self.view = view }
    }
    private static var nativeViews: [UUID: WeakView] = [:]
    static func registerNative(_ view: NSView, owner: UUID) { nativeViews[owner] = WeakView(view) }
    static func refreshNativeViews() {
        nativeViews = nativeViews.filter { $0.value.view != nil }
        nativeFrames.removeAll(); nativeVisibleFrames.removeAll(); nativeAttached.removeAll()
        func measure(_ view: NSView) {
            if let probe = view as? ToolbarMeasurementView { probe.measure() }
            for child in view.subviews { measure(child) }
        }
        // Read the actual current window hierarchy after screenshot layout,
        // rather than depending on SwiftUI/AppKit callback ordering on resize.
        for window in NSApp.windows where window.isVisible {
            if let content = window.contentView { measure(content.superview ?? content) }
        }
    }
    static func record(_ id: String, frame: CGRect, owner: UUID, kind: LayoutProbeKind = .container, group: String? = nil) {
        owners[id] = owner; frames[id] = frame; kinds[id] = kind
        if let group { groups[id] = group } else { groups.removeValue(forKey: id) }
        // AppKit can lay out a resized toolbar host before SwiftUI announces
        // its replacement geometry owner. Measure again once ownership exists.
        (nativeViews[owner]?.view as? ToolbarMeasurementView)?.measure()
    }
    static func recordNative(_ id: String, frame: CGRect, visible: CGRect, attached: Bool, owner: UUID) {
        guard owners[id] == owner else { return }
        if !attached && nativeAttached[id] == true { return }
        nativeFrames[id] = frame; nativeVisibleFrames[id] = visible; nativeAttached[id] = attached
    }
    static func remove(_ id: String, owner: UUID) {
        nativeViews.removeValue(forKey: owner)
        guard owners[id] == owner else { return }
        owners.removeValue(forKey: id); frames.removeValue(forKey: id); kinds.removeValue(forKey: id); groups.removeValue(forKey: id)
        nativeFrames.removeValue(forKey: id); nativeVisibleFrames.removeValue(forKey: id); nativeAttached.removeValue(forKey: id)
    }

    /// Checks the recorded geometry and returns one sentence per violation (empty when everything is fine).
    ///  - a probe with a group must lie inside the group's frame (1 pt tolerance);
    ///  - two `.control` probes of one group must not overlap by more than 1 pt^2;
    ///  - a `.control` must be at least `minHit` points in both directions;
    ///  - nothing may start left of the window or end right of `windowWidth`.
    /// Only probes whose id starts with `prefix` are examined; their groups are looked up regardless of the prefix.
    static func audit(prefix: String = "", minHit: CGFloat = 24, windowWidth: CGFloat? = nil) -> [String] {
        var problems: [String] = []
        var controlsByGroup: [String: [String]] = [:]
        let ids: [String] = frames.keys.filter { (id: String) -> Bool in id.hasPrefix(prefix) }.sorted()
        for id in ids {
            guard let frame = frames[id] else { continue }
            let kind: LayoutProbeKind = kinds[id] ?? .container
            if frame.minX < -1 {
                let x: Int = Int(frame.minX)
                problems.append("\(id) starts left of the window (x=\(x))")
            }
            if let windowWidth, frame.maxX > windowWidth + 1 {
                let maxX: Int = Int(frame.maxX)
                let win: Int = Int(windowWidth)
                problems.append("\(id) ends right of the window (maxX=\(maxX), window=\(win))")
            }
            if let group = groups[id], let parent = frames[group] {
                let outsideX: Bool = frame.minX < parent.minX - 1 || frame.maxX > parent.maxX + 1
                let outsideY: Bool = frame.minY < parent.minY - 1 || frame.maxY > parent.maxY + 1
                if outsideX || outsideY { problems.append("\(id) lies outside its group \(group)") }
                if kind == .control { controlsByGroup[group, default: []].append(id) }
            }
            let tooNarrow: Bool = frame.width < minHit - 0.5
            let tooShort: Bool = frame.height < minHit - 0.5
            if kind == .control && (tooNarrow || tooShort) {
                let w: Int = Int(frame.width)
                let h: Int = Int(frame.height)
                let limit: Int = Int(minHit)
                problems.append("\(id) is smaller than \(limit) pt (\(w)x\(h))")
            }
        }
        for group in controlsByGroup.keys.sorted() {
            let members: [String] = (controlsByGroup[group] ?? []).sorted()
            for first in 0..<members.count {
                for second in (first + 1)..<max(first + 1, members.count) {
                    guard let a = frames[members[first]], let b = frames[members[second]] else { continue }
                    let overlap: CGRect = a.intersection(b)
                    if !overlap.isNull && overlap.width * overlap.height > 1 {
                        problems.append("\(members[first]) overlaps \(members[second])")
                    }
                }
            }
        }
        return problems
    }
}

extension View {
    /// Records this view's frame for the packaged smoke test. `group` names another probe that must contain it.
    @ViewBuilder @MainActor func settingsLayoutProbe(_ id: String, kind: LayoutProbeKind = .container, group: String? = nil) -> some View {
        if SettingsLayoutMeasurements.isSmoke {
            modifier(SettingsMeasurementModifier(id: id, kind: kind, group: group))
        } else {
            self
        }
    }
}

@MainActor private struct SettingsMeasurementModifier: ViewModifier {
    let id: String
    let kind: LayoutProbeKind
    let group: String?
    @State private var owner = UUID()
    func body(content: Content) -> some View {
        content.background {
            GeometryReader { geometry in
                let frame = geometry.frame(in: .global)
                Color.clear
                    .onAppear { SettingsLayoutMeasurements.record(id, frame: frame, owner: owner, kind: kind, group: group) }
                    .onChange(of: frame) { _, next in SettingsLayoutMeasurements.record(id, frame: next, owner: owner, kind: kind, group: group) }
            }
        }.background {
            if id.hasPrefix("toolbar.") { ToolbarMeasurement(id: id, owner: owner).frame(maxWidth: .infinity, maxHeight: .infinity) }
        }.onDisappear { SettingsLayoutMeasurements.remove(id, owner: owner) }
    }
}

// Toolbar items have separate SwiftUI hosting views, so their `.global`
// coordinates cannot prove whether the actual native control is on screen.
@MainActor private struct ToolbarMeasurement: NSViewRepresentable {
    let id: String
    let owner: UUID
    func makeNSView(context: Context) -> ToolbarMeasurementView { ToolbarMeasurementView(id: id, owner: owner) }
    func updateNSView(_ view: ToolbarMeasurementView, context: Context) {
        view.id = id; view.owner = owner
        SettingsLayoutMeasurements.registerNative(view, owner: owner)
        DispatchQueue.main.async { [weak view] in view?.measure() }
    }
}

@MainActor private final class ToolbarMeasurementView: NSView {
    var id: String
    var owner: UUID
    init(id: String, owner: UUID) {
        self.id = id; self.owner = owner; super.init(frame: .zero)
        SettingsLayoutMeasurements.registerNative(self, owner: owner)
    }
    required init?(coder: NSCoder) { return nil }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func layout() { super.layout(); measure() }
    override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); measure() }
    func measure() {
        SettingsLayoutMeasurements.recordNative(id, frame: convert(bounds, to: nil), visible: convert(visibleRect, to: nil), attached: window != nil && !isHiddenOrHasHiddenAncestor, owner: owner)
    }
}
