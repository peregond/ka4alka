import AppKit
import SwiftUI

/// Works on macOS 14 as well as 26. Scroll events only publish when visibility
/// changes; scrolling never writes coordinates into the catalog's SwiftUI state.
struct HeroVisibilityProbe: NSViewRepresentable {
    var changed: (Bool) -> Void
    func makeNSView(context: Context) -> HeroVisibilityView {
        let view = HeroVisibilityView()
        view.changed = changed
        return view
    }
    func updateNSView(_ view: HeroVisibilityView, context: Context) {
        view.changed = changed
        view.report()
    }
    static func dismantleNSView(_ view: HeroVisibilityView, coordinator: ()) { view.stop() }
}

final class HeroVisibilityView: NSView {
    var changed: ((Bool) -> Void)?
    private var observations: [NSObjectProtocol] = []
    private var reported: Bool?
    private var pending = false
    override var isFlipped: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        stop()
        guard window != nil else { report(); return }
        var ancestor: NSView? = superview
        while let view = ancestor {
            if let clip = view as? NSClipView {
                clip.postsBoundsChangedNotifications = true
                observations.append(NotificationCenter.default.addObserver(forName: NSView.boundsDidChangeNotification, object: clip, queue: .main) { [weak self] _ in
                    self?.report()
                })
            }
            ancestor = view.superview
        }
        report()
    }
    override func layout() { super.layout(); report() }

    func stop() {
        observations.forEach { NotificationCenter.default.removeObserver($0) }
        observations.removeAll()
    }
    func report() {
        guard !pending else { return }
        pending = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.pending = false
            let rect = self.visibleRect.intersection(self.bounds)
            let visible = self.window != nil && !self.isHiddenOrHasHiddenAncestor
                && rect.width > 0 && rect.height >= min(160, self.bounds.height * 0.5) && self.bounds.height > 0
            guard visible != self.reported else { return }
            self.reported = visible
            self.changed?(visible)
        }
    }
    deinit { observations.forEach { NotificationCenter.default.removeObserver($0) } }
}
