import SwiftUI

struct RecentSearch: Equatable, Identifiable {
    var query: String
    var id: String { query.lowercased() }
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
    @State private var compactToolbar = true
    var body: some View {
        VStack(spacing: 0) {
            if model.submittedQuery.isEmpty { recentSearches } else { CatalogView() }
        }
        .settingsLayoutProbe("search.content")
        .background { SearchToolbarLayoutObserver { compactToolbar = $0 } }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                Button { model.back() } label: { Label(tr("Назад", "Back"), systemImage: "chevron.backward") }
                    .buttonStyle(.automatic)
            }
            ToolbarItem(placement: .principal) {
                NativeSearchField(text: $model.query, focusToken: model.searchFocus,
                                  prompt: tr("Найти фильм, сериал или игру", "Find a movie, series or game")) { model.submitSearch() }
                    .frame(width: compactToolbar ? 240 : 420)
                    .settingsLayoutProbe("search.field")
            }
        }
        .onChange(of: model.query) { _, query in if query.isEmpty { model.clearSearch() } }
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
                                model.query = entry.query
                                model.submitSearch()
                            } label: {
                                Label(entry.query, systemImage: "clock.arrow.circlepath")
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
