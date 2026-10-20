import AppKit
import SwiftUI

/// One observer for the current page's vertical clip view. It publishes only threshold crossings,
/// never scroll pixels, and never observes the sidebar or horizontal poster shelves.
struct ShellScrollObserver: NSViewRepresentable {
    var routeKey: String
    var onChange: (Bool) -> Void
    func makeNSView(context: Context) -> Probe { Probe() }
    func updateNSView(_ view: Probe, context: Context) {
        view.configure(routeKey: routeKey, onChange: onChange)
    }
    static func dismantleNSView(_ view: Probe, coordinator: ()) { view.detach(); view.stopToolbarObservation() }

    @MainActor final class Probe: NSView {
        private var routeKey = ""
        private var callback: ((Bool) -> Void)?
        private weak var clip: NSClipView?
        private var observation: NSObjectProtocol?
        private weak var nativeToolbar: NSToolbar?
        private var toolbarObservation: NSKeyValueObservation?
        private var queued = false
        private var shown = false
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); schedule() }
        override func layout() { super.layout(); schedule() }
        func configure(routeKey: String, onChange: @escaping (Bool) -> Void) {
            callback = onChange
            if self.routeKey != routeKey {
                self.routeKey = routeKey; detach(); shown = false
                DispatchQueue.main.async { [weak self] in self?.callback?(false) }
            }
            schedule()
        }
        func detach() {
            if let observation { NotificationCenter.default.removeObserver(observation) }
            observation = nil; clip = nil
        }
        func stopToolbarObservation() { toolbarObservation?.invalidate(); toolbarObservation = nil; nativeToolbar = nil }
        /// NavigationSplitView can restore its native toolbar after SwiftUI's visibility modifier.
        /// The page uses overlay controls, so keep the actual AppKit toolbar hidden as well.
        private func hideNativeToolbar() {
            guard let window else { return }
            if window.titleVisibility != .hidden { window.titleVisibility = .hidden }
            if !window.titlebarAppearsTransparent { window.titlebarAppearsTransparent = true }
            guard let toolbar = window.toolbar else { return }
            if nativeToolbar !== toolbar {
                stopToolbarObservation(); nativeToolbar = toolbar
                toolbarObservation = toolbar.observe(\.isVisible, options: [.new]) { [weak self] _, change in
                    guard change.newValue == true else { return }
                    DispatchQueue.main.async { self?.hideNativeToolbar() }
                }
            }
            if toolbar.isVisible { toolbar.isVisible = false }
        }
        private func schedule() {
            guard !queued else { return }
            queued = true
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.queued = false; self.attach()
            }
        }
        private func attach() {
            hideNativeToolbar()
            guard let content = window?.contentView else { return }
            var candidates: [NSScrollView] = []
            func visit(_ view: NSView) {
                if let scroll = view as? NSScrollView,
                   !scroll.isHiddenOrHasHiddenAncestor, scroll.bounds.width > 280, scroll.bounds.height > 100,
                   scroll.documentView?.bounds.height ?? 0 > scroll.contentView.bounds.height + 1 {
                    candidates.append(scroll)
                }
                for child in view.subviews { visit(child) }
            }
            visit(content)
            guard let scroll = candidates.max(by: { $0.bounds.width * $0.bounds.height < $1.bounds.width * $1.bounds.height }) else {
                detach()
                if shown { shown = false; callback?(false) }
                return
            }
            let next = scroll.contentView
            if clip !== next {
                detach(); clip = next; next.postsBoundsChangedNotifications = true
                observation = NotificationCenter.default.addObserver(forName: NSView.boundsDidChangeNotification, object: next, queue: .main) { [weak self] _ in
                    MainActor.assumeIsolated { self?.measure() }
                }
            }
            measure()
        }
        private func measure() {
            guard let clip, let scroll = clip.enclosingScrollView, let document = scroll.documentView else { return }
            let top = document.isFlipped ? clip.bounds.minY - document.bounds.minY + scroll.contentInsets.top
                : document.bounds.maxY - clip.bounds.maxY + scroll.contentInsets.top
            let next = ShellChromeVisibility.visible(offset: top, wasVisible: shown)
            guard next != shown else { return }
            shown = next
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.callback?(self.shown)
            }
        }
    }
}

enum ShellChromeVisibility {
    /// Hysteresis prevents trackpad bounce near the top from repeatedly rebuilding the toolbar.
    static func visible(offset: CGFloat, wasVisible: Bool) -> Bool {
        wasVisible ? offset > 8 : offset >= 56
    }
}
