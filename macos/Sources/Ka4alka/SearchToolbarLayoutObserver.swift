import AppKit
import SwiftUI

/// Native toolbar overflow depends on the actual window width. A SwiftUI content
/// proposal can retain its old width while AppKit is resizing separate toolbar hosts.
/// Publish only the compact/wide transition, never a stream of resize pixels.
struct SearchToolbarLayoutObserver: NSViewRepresentable {
    var onChange: (Bool) -> Void
    func makeNSView(context: Context) -> Probe { Probe() }
    func updateNSView(_ view: Probe, context: Context) { view.configure(onChange) }
    static func dismantleNSView(_ view: Probe, coordinator: ()) { view.detach() }

    @MainActor final class Probe: NSView {
        private weak var observedWindow: NSWindow?
        private var observation: NSObjectProtocol?
        private var callback: ((Bool) -> Void)?
        private var compact: Bool?
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); attach() }
        func configure(_ callback: @escaping (Bool) -> Void) {
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
            let next = window.frame.width < 1100
            guard next != compact else { return }
            compact = next
            DispatchQueue.main.async { [weak self] in self?.callback?(next) }
        }
    }
}
