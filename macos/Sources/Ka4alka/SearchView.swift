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
    @FocusState private var searchFocused: Bool
    @State private var contentWidth: CGFloat = 0

    var body: some View {
        if #available(macOS 15.0, *) {
            page
                .searchable(text: $model.query, placement: .toolbar,
                            prompt: tr("Найти фильм, сериал или игру", "Find a movie, series or game"))
                .searchPresentationToolbarBehavior(.avoidHidingContent)
                .searchFocused($searchFocused)
                .onSubmit(of: .search) { model.submitSearch() }
                .task(id: model.searchFocus) {
                    await Task.yield()
                    searchFocused = true
                }
        } else {
            // macOS 14 predates searchFocused. Keep its native AppKit editor and
            // focus coordinator in the system toolbar rather than in page content.
            page.toolbar {
                ToolbarItem(placement: .principal) {
                    NativeSearchField(text: $model.query, focusToken: model.searchFocus,
                                      prompt: tr("Найти фильм, сериал или игру", "Find a movie, series or game")) { model.submitSearch() }
                        .frame(minWidth: 220, idealWidth: 300, maxWidth: 420)
                }
            }
        }
    }

    private var page: some View {
        VStack(spacing: 0) {
            if model.submittedQuery.isEmpty { recentSearches } else { CatalogView() }
        }
        .settingsLayoutProbe("search.content")
        .kdReadWidth($contentWidth)
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button { model.back() } label: {
                    Label(tr("Назад", "Back"), systemImage: "chevron.backward")
                }
                .buttonStyle(.automatic)
                .help(tr("Назад", "Back"))
            }
            ToolbarItem(placement: .primaryAction) { scope }
        }
        .onChange(of: model.query) { _, query in
            if query.isEmpty { model.clearSearch() }
        }
    }

    @ViewBuilder private var scope: some View {
        if contentWidth >= 800 {
            scopePicker.pickerStyle(.segmented).frame(width: 245)
                .settingsLayoutProbe("search.scope")
        } else {
            scopePicker.pickerStyle(.menu).frame(width: 140)
                .settingsLayoutProbe("search.scope")
        }
    }
    private var scopePicker: some View {
        Picker(tr("Где искать", "Search in"), selection: Binding(
            get: { model.section == .games },
            set: { model.setSearchGames($0) }
        )) {
            Text(tr("Кино и сериалы", "Movies & series")).tag(false)
            Text(tr("Игры", "Games")).tag(true)
        }
        .labelsHidden()
        .controlSize(.regular)
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
