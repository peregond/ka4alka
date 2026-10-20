import AppKit
import SwiftUI

/// Focus after attaching to the real window, only for a new Search page or explicit Command-F.
/// Typing never sends a network request; the native Return action submits the completed query.
struct NativeSearchField: NSViewRepresentable {
    @Binding var text: String
    @Binding var focused: Bool
    var focusToken: Int
    var prompt: String
    var submit: () -> Void
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    func makeNSView(context: Context) -> Field {
        let field = Field()
        field.delegate = context.coordinator
        field.target = context.coordinator
        field.action = #selector(Coordinator.search(_:))
        field.isBezeled = false; field.isBordered = false; field.drawsBackground = false
        field.focusRingType = .none
        field.font = NSFont.preferredFont(forTextStyle: .body)
        field.textColor = .labelColor
        field.sendsWholeSearchString = true; field.sendsSearchStringImmediately = false
        if let cell = field.cell as? NSSearchFieldCell { cell.searchButtonCell = nil; cell.cancelButtonCell = nil }
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        field.setAccessibilityIdentifier("search-field")
        field.attached = { [weak coordinator = context.coordinator, weak field] in
            if let field { coordinator?.requestFocus(field) }
        }
        return field
    }
    func updateNSView(_ field: Field, context: Context) {
        context.coordinator.parent = self
        if field.stringValue != text { field.stringValue = text }
        field.placeholderString = prompt
        field.setAccessibilityLabel(prompt)
        context.coordinator.requestFocus(field)
    }
    @MainActor final class Field: NSSearchField {
        var attached: (() -> Void)?
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); if window != nil { attached?() } }
    }
    @MainActor final class Coordinator: NSObject, NSSearchFieldDelegate {
        var parent: NativeSearchField
        private var appliedFocus: Int?
        private var queued = false
        init(_ parent: NativeSearchField) { self.parent = parent }
        func requestFocus(_ field: Field) {
            guard field.window != nil, appliedFocus != parent.focusToken, !queued else { return }
            queued = true
            DispatchQueue.main.async { [weak self, weak field] in
                guard let self else { return }
                self.queued = false
                guard let field, let window = field.window, !field.isHiddenOrHasHiddenAncestor else { return }
                if window.makeFirstResponder(field) { self.appliedFocus = self.parent.focusToken }
            }
        }
        @objc func search(_ sender: NSSearchField) { parent.text = sender.stringValue; parent.submit() }
        func controlTextDidChange(_ notification: Notification) {
            if let field = notification.object as? NSSearchField { parent.text = field.stringValue }
        }
        func controlTextDidBeginEditing(_ notification: Notification) { parent.focused = true }
        func controlTextDidEndEditing(_ notification: Notification) { parent.focused = false }
    }
}
