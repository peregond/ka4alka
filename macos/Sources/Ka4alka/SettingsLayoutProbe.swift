import AppKit
import SwiftUI

// Capture real SwiftUI layout in the packaged native smoke test. Normal app
// launches do not install geometry readers or retain this diagnostic state.
@MainActor enum SettingsLayoutMeasurements {
    static var frames: [String: CGRect] = [:]
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
    static func record(_ id: String, frame: CGRect, owner: UUID) {
        owners[id] = owner; frames[id] = frame
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
        owners.removeValue(forKey: id); frames.removeValue(forKey: id)
        nativeFrames.removeValue(forKey: id); nativeVisibleFrames.removeValue(forKey: id); nativeAttached.removeValue(forKey: id)
    }
}

extension View {
    @ViewBuilder @MainActor func settingsLayoutProbe(_ id: String) -> some View {
        if CommandLine.arguments.contains("--smoke-test") {
            modifier(SettingsMeasurementModifier(id: id))
        } else {
            self
        }
    }
}

@MainActor private struct SettingsMeasurementModifier: ViewModifier {
    let id: String
    @State private var owner = UUID()
    func body(content: Content) -> some View {
        content.background {
            GeometryReader { geometry in
                let frame = geometry.frame(in: .global)
                Color.clear
                    .onAppear { SettingsLayoutMeasurements.record(id, frame: frame, owner: owner) }
                    .onChange(of: frame) { _, next in SettingsLayoutMeasurements.record(id, frame: next, owner: owner) }
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
