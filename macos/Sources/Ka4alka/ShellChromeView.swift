import AppKit
import SwiftUI

/// The window toolbar. It does not change while a page scrolls, so nothing appears, disappears or is rebuilt
/// mid-scroll. What it shows depends on the page:
///  - a page that opens with a full-bleed banner (`heroPage`): the banner runs under the toolbar, which has no bar and
///    no title there; the sidebar marks the open section and the system's soft scroll-edge effect keeps the buttons readable;
///  - every other page: the system's toolbar bar with the name of the page (the title, or the name of the open title or
///    person) and the system's automatic scroll edge, so text never runs under the buttons.
struct ShellChromeView: ViewModifier {
    @EnvironmentObject private var model: AppModel
    @Environment(\.colorScheme) private var colorScheme
    var title: String
    var heroPage: Bool

    func body(content: Content) -> some View {
        content
            // The window is named for assistive technology either way (VoiceOver, Mission Control).
            .navigationTitle(heroPage ? "" : title)
            .background(ShellWindowName(name: title))
            // Keep the system toolbar alive even when its page actions are absent.
            // Hiding it also hides traffic lights on some macOS versions.
            .toolbar(.visible, for: .windowToolbar)
            .kdToolbarBarHidden(heroPage)
            .toolbar {
                #if compiler(>=6.2)
                ShellBackLeadingGap(visible: model.sidebarVisibility == .detailOnly && model.route != .search && model.routes.count > 1)
                #endif
                ToolbarItem(placement: .navigation) {
                    if model.route != .search, model.routes.count > 1 {
                        Button { model.back() } label: {
                            Label(tr("Назад", "Back"), systemImage: "chevron.backward")
                        }
                        .buttonStyle(.automatic)
                        .help(tr("Назад", "Back"))
                    }
                }
                ToolbarItemGroup(placement: .primaryAction) {
                    if model.route != .search {
                        Button { model.addTorrent = true } label: {
                            Label(tr("Добавить торрент", "Add Torrent"), systemImage: "plus")
                        }
                        .buttonStyle(.automatic)
                        .settingsLayoutProbe("toolbar.add")
                        .help(tr("Добавить торрент", "Add Torrent"))

                        Button { Task { await model.refresh() } } label: {
                            Label(tr("Обновить каталог", "Refresh Catalog"), systemImage: "arrow.clockwise")
                        }
                        .disabled(model.refreshing)
                        .buttonStyle(.automatic)
                        .settingsLayoutProbe("toolbar.refresh")
                        .help(tr("Обновить каталог", "Refresh Catalog"))

                        Button {
                            model.setAppearance(colorScheme == .dark ? .light : .dark)
                        } label: {
                            Label(tr("Сменить тему", "Change Appearance"), systemImage: colorScheme == .light ? "sun.max" : "moon")
                        }
                        .buttonStyle(.automatic)
                        .settingsLayoutProbe("toolbar.appearance")
                        .help(tr("Сменить тему", "Change Appearance"))
                    }
                }
            }
    }
}

#if compiler(>=6.2)
/// With the sidebar hidden its toggle sits next to Back in the window toolbar. A fixed gap keeps Back a separate
/// control instead of one glass group with the toggle (macOS 26 and later).
struct ShellBackLeadingGap: ToolbarContent {
    var visible: Bool
    var body: some ToolbarContent {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            if visible { ToolbarSpacer(.fixed, placement: .navigation) }
        }
        #endif
    }
}
#endif

/// A page with a banner shows no title in the toolbar (the banner runs under it), but the window still has a name for
/// assistive technology and Mission Control: the open section, title or person.
private struct ShellWindowName: NSViewRepresentable {
    var name: String
    func makeNSView(context: Context) -> NSView { Probe() }
    func updateNSView(_ view: NSView, context: Context) {
        guard let probe = view as? Probe else { return }
        probe.name = name
        probe.apply()
    }
    private final class Probe: NSView {
        var name = ""
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); apply() }
        func apply() { window?.setAccessibilityTitle(name) }
    }
}
