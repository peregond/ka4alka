import SwiftUI

/// Актуальное starts with the banner, then top stories, rubrics and shelves.
struct FeaturedView: View {
    @EnvironmentObject private var model: AppModel
    /// The rubric at the top of the visible area. Saved to the model when the page goes away, so Back returns to the same place.
    @State private var anchor: String?

    private func visibleSlices() -> [FeaturedGroupSlice] {
        var rows: [FeaturedGroupSlice] = []
        for group in model.curated {
            let cards: [MediaCard] = model.visible(group.items)
            if !cards.isEmpty { rows.append(FeaturedGroupSlice(group: group, cards: cards)) }
        }
        return rows
    }

    var body: some View {
        let slices: [FeaturedGroupSlice] = visibleSlices()
        let plan = FeaturedPlan(slices: slices)
        ScrollView {
            // The banner is outside the centred column: it runs from edge to edge and up under the toolbar.
            VStack(spacing: 0) {
                heroSection(slices: slices, plan: plan)
                LazyVStack(alignment: .leading, spacing: KD.Space.xl) {
                    content(slices: slices, plan: plan)
                }
                .scrollTargetLayout()
                .padding(.top, KD.Space.xl)
                .padding(.bottom, KD.gutter)
                .frame(maxWidth: KD.Width.page)
                .frame(maxWidth: .infinity)
            }
        }
        // With the banner first, the page starts at the top of the window, under the toolbar.
        .ignoresSafeArea(edges: .top)
        .scrollPosition(id: $anchor, anchor: .top)
        .onAppear { if anchor == nil { anchor = model.featuredAnchor } }
        .onDisappear { model.featuredAnchor = anchor }
        .task(id: model.revision) { await model.loadFeatured() }
    }

    /// The banner, or its placeholder while the first page is loading.
    @ViewBuilder private func heroSection(slices: [FeaturedGroupSlice], plan: FeaturedPlan) -> some View {
        if slices.isEmpty {
            if showsLoadingHero {
                HeroPlaceholder()
                    .settingsLayoutProbe("featured.hero")
            }
        } else if !plan.banner.isEmpty {
            CatalogSpotlightView(items: plan.banner)
                .id("featured-hero")
                .settingsLayoutProbe("featured.hero")
        }
    }

    private var showsLoadingHero: Bool {
        guard model.curated.isEmpty else { return false }
        switch model.featuredState {
        case .failed, .empty: return false
        default: return true
        }
    }

    @ViewBuilder private func content(slices: [FeaturedGroupSlice], plan: FeaturedPlan) -> some View {
        if slices.isEmpty {
            emptyContent
        } else {
            if !plan.digest.isEmpty {
                FeaturedDigest(stories: plan.digest)
                    .id("featured-digest")
                    .settingsLayoutProbe("featured.digest")
            }
            ForEach(plan.display) { slice in
                group(slice).id(slice.id)
            }
        }
    }

    @ViewBuilder private func group(_ slice: FeaturedGroupSlice) -> some View {
        if slice.group.layout == "lead" {
            FeaturedLeadRubric(slice: slice)
        } else if slice.group.layout == "ranking" {
            FeaturedRanking(group: slice.group, state: $model.rankingState)
                .settingsLayoutProbe("featured.ranking")
        } else {
            FeaturedShelfSection(slice: slice)
        }
    }

    // MARK: States

    /// Nothing to show: still loading, the engine failed, the catalog is empty, or every title is hidden.
    @ViewBuilder private var emptyContent: some View {
        if !model.curated.isEmpty {
            KDEmptyState(
                title: tr("Все подборки скрыты", "All collections are hidden"),
                systemImage: "eye.slash",
                message: tr("Скрытые названия можно вернуть в настройках.", "Hidden titles can be restored in Settings.")
            )
            .frame(maxWidth: .infinity, minHeight: 320)
        } else {
            switch model.featuredState {
            case .failed(let message):
                KDEmptyState(
                    title: tr("Не удалось загрузить подборки", "Could not load collections"),
                    systemImage: "rectangle.stack.badge.minus",
                    message: message,
                    actionTitle: tr("Повторить", "Retry"),
                    action: { Task { await model.loadFeatured(force: true) } }
                )
                .frame(maxWidth: .infinity, minHeight: 320)
            case .empty:
                KDEmptyState(
                    title: tr("Подборки пока пусты", "No collections yet"),
                    systemImage: "rectangle.stack",
                    message: tr("Каталог ещё готовится. Загляни сюда чуть позже.", "The catalog is still being prepared. Check back in a moment."),
                    actionTitle: tr("Обновить", "Reload"),
                    action: { Task { await model.loadFeatured(force: true) } }
                )
                .frame(maxWidth: .infinity, minHeight: 320)
            default:
                loadingContent
            }
        }
    }

    @ViewBuilder private var loadingContent: some View {
        KDShelfSkeleton()
            .padding(.horizontal, KD.gutter)
        KDShelfSkeleton()
            .padding(.horizontal, KD.gutter)
    }
}
