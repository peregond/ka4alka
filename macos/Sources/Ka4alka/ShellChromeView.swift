import SwiftUI

/// Only threshold crossings update the window toolbar. The page keeps its own identity,
/// so scrolling does not rebuild the catalog, posters or hero.
struct ShellChromeView: ViewModifier {
    @EnvironmentObject private var model: AppModel
    @Environment(\.colorScheme) private var colorScheme
    @State private var scrolled = false
    var title: String

    func body(content: Content) -> some View {
        content
            .navigationTitle("")
            // Keep the system toolbar alive even when its page actions are absent.
            // Hiding it also hides traffic lights on some macOS versions.
            .toolbar(.visible, for: .windowToolbar)
            .toolbar {
                ToolbarItem(placement: .navigation) {
                    if model.route != .search, model.routes.count > 1 {
                        Button { model.back() } label: {
                            Label(tr("Назад", "Back"), systemImage: "chevron.backward")
                        }
                        .buttonStyle(.automatic)
                        .help(tr("Назад", "Back"))
                    }
                }
                ToolbarItem(placement: .principal) {
                    if model.route != .search, scrolled {
                        Text(title).font(.headline).lineLimit(1)
                    }
                }
                ToolbarItemGroup(placement: .primaryAction) {
                    if model.route != .search, scrolled {
                        Button { model.addTorrent = true } label: {
                            Label(tr("Добавить торрент", "Add torrent"), systemImage: "plus")
                        }
                        .buttonStyle(.automatic)
                        .settingsLayoutProbe("toolbar.add")
                        .help(tr("Добавить торрент", "Add torrent"))

                        Button { Task { await model.refresh() } } label: {
                            Label(tr("Обновить каталог", "Refresh catalog"), systemImage: "arrow.clockwise")
                        }
                        .disabled(model.refreshing)
                        .buttonStyle(.automatic)
                        .settingsLayoutProbe("toolbar.refresh")
                        .help(tr("Обновить каталог", "Refresh catalog"))

                        Button {
                            model.setAppearance(colorScheme == .dark ? .light : .dark)
                        } label: {
                            Label(tr("Сменить тему", "Change appearance"), systemImage: colorScheme == .light ? "sun.max" : "moon")
                        }
                        .buttonStyle(.automatic)
                        .settingsLayoutProbe("toolbar.appearance")
                        .help(tr("Сменить тему", "Change appearance"))
                    }
                }
            }
            .background {
                ShellScrollObserver(routeKey: model.route.key) { next in
                    if scrolled != next { scrolled = next }
                }
            }
    }
}
