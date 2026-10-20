import SwiftUI

struct RecentSearch: Equatable, Identifiable {
    var query: String
    var games: Bool
    var id: String { (games ? "games:" : "video:") + query }
}

struct SearchOrigin {
    var section: Section
    var query: String
    var submittedQuery: String
    var filter: CatalogFilter
    var page: Int
    var anchor: String?
    var list: CatalogResult?
}

/// The sidebar opens this page with the field already focused. Typing changes only the draft;
/// Return submits one request, so network work never competes with text input or scrolling.
struct SearchView: View {
    @EnvironmentObject private var model: AppModel
    @State private var focused = false
    @State private var headerWidth: CGFloat = 0

    var body: some View {
        VStack(spacing: 0) {
            header
            if model.submittedQuery.isEmpty { recentSearches } else { CatalogView() }
        }
        .onChange(of: model.query) { _, query in
            if query.isEmpty { model.clearSearch() }
        }
    }

    private var header: some View {
        HStack(alignment: .top, spacing: KD.Space.s) {
            KDIconButton(systemImage: "chevron.backward", label: tr("Назад", "Back")) { model.back() }
                .padding(.top, 8)
            // Keep one editor while changing layout; duplicate focus bindings lose the responder.
            let layout = headerWidth >= 680 ? AnyLayout(HStackLayout(spacing: KD.Space.m)) : AnyLayout(VStackLayout(spacing: KD.Space.s))
            layout {
                field
                scope.frame(maxWidth: headerWidth >= 680 ? 260 : .infinity)
            }
        }
        .padding(KD.gutter)
        .frame(maxWidth: .infinity)
        .kdReadWidth($headerWidth)
        .background(KDPalette.pageBackground)
        .settingsLayoutProbe("search.header")
    }
    private var field: some View {
        HStack(spacing: KD.Space.xs) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary).accessibilityHidden(true)
            NativeSearchField(text: $model.query, focused: $focused, focusToken: model.searchFocus,
                              prompt: tr("Найти фильм, сериал или игру", "Find a movie, series or game")) { model.submitSearch() }
                .frame(height: 28)
            if !model.query.isEmpty {
                KDIconButton(systemImage: "xmark.circle.fill", label: tr("Очистить поиск", "Clear search")) {
                    model.clearSearch(); model.searchFocus += 1
                }
            }
        }
        .padding(.horizontal, KD.Space.m)
        .frame(minWidth: 220, minHeight: 44)
        .kdSurface(in: Capsule())
        .overlay(Capsule().strokeBorder(focused ? KDPalette.accent : .clear, lineWidth: 2).allowsHitTesting(false))
        .settingsLayoutProbe("search.field")
    }
    private var scope: some View {
        Picker(tr("Где искать", "Search in"), selection: Binding(
            get: { model.section == .games },
            set: { model.setSearchGames($0) }
        )) {
            Text(tr("Кино и сериалы", "Movies & series")).tag(false)
            Text(tr("Игры", "Games")).tag(true)
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .frame(minWidth: 245)
        .settingsLayoutProbe("search.scope")
    }
    private var recentSearches: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: KD.Space.m) {
                if model.recentSearches.isEmpty {
                    KDEmptyState(title: tr("Что будем искать?", "What are you looking for?"), systemImage: "magnifyingglass",
                                 message: tr("Введи название и нажми Return.", "Enter a title and press Return."))
                        .frame(maxWidth: .infinity, minHeight: 240)
                } else {
                    HStack {
                        Text(tr("Недавние поиски", "Recent searches")).font(KDFont.section)
                        Spacer()
                        Button(tr("Очистить", "Clear")) { model.recentSearches.removeAll() }
                            .buttonStyle(KDSecondaryButtonStyle())
                    }
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: KD.Space.s)], spacing: KD.Space.s) {
                        ForEach(model.recentSearches) { entry in
                            Button {
                                model.setSearchGames(entry.games); model.query = entry.query
                                model.submitSearch()
                            } label: {
                                Label(entry.query, systemImage: entry.games ? "gamecontroller" : "clock.arrow.circlepath")
                                    .lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .buttonStyle(KDSecondaryButtonStyle())
                        }
                    }
                }
            }
            .padding(KD.gutter)
        }
    }
}
