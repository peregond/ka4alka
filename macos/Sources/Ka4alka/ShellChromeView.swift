import SwiftUI

/// Scroll state belongs to this small overlay, so crossing the threshold never invalidates
/// the catalog, sidebar, poster grid or hero. One shared glass surface avoids nested live blurs.
struct ShellChromeView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.colorScheme) private var colorScheme
    @State private var scrolled = false
    var title: String
    var body: some View {
        VStack(spacing: 0) {
                if model.route != .search {
                    if scrolled { bar.padding(KD.Space.s) }
                    else if model.routes.count > 1 {
                        HStack {
                            KDIconButton(systemImage: "chevron.backward", label: tr("Назад", "Back")) { model.back() }
                            Spacer()
                        }.padding(KD.Space.m)
                    }
                }
        }
            .frame(maxWidth: .infinity, alignment: .top)
            .background {
                ShellScrollObserver(routeKey: model.route.key) { next in
                    if scrolled != next { scrolled = next }
                }
            }
    }
    private var bar: some View {
        HStack(spacing: KD.Space.xs) {
            if model.routes.count > 1 {
                KDToolbarButton(title: tr("Назад", "Back"), systemImage: "chevron.backward") { model.back() }
            }
            Text(title).font(.headline).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
            KDToolbarButton(title: tr("Добавить торрент", "Add torrent"), systemImage: "plus") { model.addTorrent = true }
                .settingsLayoutProbe("toolbar.add")
            KDToolbarButton(title: tr("Обновить каталог", "Refresh catalog"), systemImage: "arrow.clockwise", isDisabled: model.refreshing) { Task { await model.refresh() } }
                .settingsLayoutProbe("toolbar.refresh")
            KDToolbarButton(title: tr("Сменить тему", "Change appearance"), systemImage: colorScheme == .light ? "sun.max" : "moon") {
                model.setAppearance(colorScheme == .dark ? .light : .dark)
            }.settingsLayoutProbe("toolbar.appearance")
        }
        .padding(KD.Space.s)
        .kdSurface(in: Capsule())
    }
}
