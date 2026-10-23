import AppKit
import SwiftUI

/// Native centered search width depends on the actual window width. A SwiftUI content
/// proposal can retain its old width while AppKit is resizing separate toolbar hosts.
/// Publish width steps rather than a stream of resize pixels.
struct SearchToolbarLayoutObserver: NSViewRepresentable {
    var onChange: (CGFloat) -> Void
    // Reserve the sidebar, native navigation and trailing toolbar controls.
    // A finite width prevents the principal item from pushing native controls away.
    static func fieldWidth(windowWidth: CGFloat) -> CGFloat {
        min(1120, max(176, floor((windowWidth - 540) / 32) * 32))
    }
    func makeNSView(context: Context) -> Probe { Probe() }
    func updateNSView(_ view: Probe, context: Context) { view.configure(onChange) }
    static func dismantleNSView(_ view: Probe, coordinator: ()) { view.detach() }

    @MainActor final class Probe: NSView {
        private weak var observedWindow: NSWindow?
        private var observation: NSObjectProtocol?
        private var callback: ((CGFloat) -> Void)?
        private var fieldWidth: CGFloat?
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); attach() }
        func configure(_ callback: @escaping (CGFloat) -> Void) {
            self.callback = callback
            attach()
        }
        func detach() {
            if let observation { NotificationCenter.default.removeObserver(observation) }
            observation = nil; observedWindow = nil
        }
        private func attach() {
            guard let window else { detach(); return }
            if observedWindow !== window {
                detach(); observedWindow = window
                observation = NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification,
                                                                     object: window, queue: .main) { [weak self] _ in
                    MainActor.assumeIsolated { self?.measure() }
                }
            }
            measure()
        }
        private func measure() {
            guard let window else { return }
            let next = SearchToolbarLayoutObserver.fieldWidth(windowWidth: window.frame.width)
            guard next != fieldWidth else { return }
            fieldWidth = next
            DispatchQueue.main.async { [weak self] in self?.callback?(next) }
        }
    }
}
