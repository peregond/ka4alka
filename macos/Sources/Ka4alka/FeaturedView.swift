import SwiftUI

struct FeaturedView: View {
    @EnvironmentObject private var model: AppModel
    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 30) {
                HStack { VStack(alignment: .leading, spacing: 6) { Text(tr("Актуальное", "Featured")).font(.largeTitle.bold()); Text(tr("Выбираем истории, в которые стоит погрузиться.", "Stories and worlds worth exploring.")).foregroundStyle(.secondary) }; Spacer(); if model.loading { ProgressView().controlSize(.small) }; Button { Task { await model.refresh(); await model.loadFeatured() } } label: { Image(systemName: "arrow.clockwise") } }
                if model.curated.isEmpty { SkeletonGrid() }
                ForEach(model.curated) { group in
                    VStack(alignment: .leading, spacing: 12) {
                        HStack { Button { model.openCollection(group.id) } label: { HStack { Text(group.title).font(.title2.bold()); Image(systemName: "chevron.right").font(.callout.weight(.semibold)) } }.buttonStyle(.plain); Spacer(); Button(tr("Ещё", "More")) { model.openCollection(group.id) }.buttonStyle(.borderless) }
                        if !group.description.isEmpty { Text(group.description).font(.callout).foregroundStyle(.secondary) }
                        if group.layout == "lead", let lead = group.items.first { FeaturedHero(card: lead) }
                        ScrollView(.horizontal) {
                            LazyHStack(alignment: .top, spacing: 16) { ForEach(Array(model.visible(group.items).enumerated()), id: \.element.id) { index, card in PosterCard(card: card, rank: group.layout == "ranking" ? index + 1 : nil).frame(width: 155) } }.padding(.vertical, 4)
                        }.scrollIndicators(.hidden)
                    }
                }
            }.padding(24)
        }.task(id: model.revision) { await model.loadFeatured() }
    }
}
struct AwardPage: Decodable { var years: [Int]; var items: [MediaCard]; var winners: [MediaCard]?; var nominees: [MediaCard]? }
struct CollectionView: View {
    @EnvironmentObject private var model: AppModel
    var id: String
    @State private var award: AwardPage?
    @State private var year = 0
    @State private var page = 1
    private var isAward: Bool { id.hasPrefix("award-") }
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text(model.list?.title ?? model.curated.first { $0.id == id }?.title ?? tr("Подборка", "Collection")).font(.largeTitle.bold())
                if let description = model.list?.description, !description.isEmpty { Text(description).foregroundStyle(.secondary) }
                if isAward, let award {
                    Picker(tr("Год", "Year"), selection: $year) { Text(tr("Все годы", "All years")).tag(0); ForEach(award.years, id: \.self) { Text(String($0)).tag($0) } }.pickerStyle(.menu).frame(width: 200)
                    if let winners = award.winners { Text(tr("Победители", "Winners")).font(.title2.bold()); PosterGrid(items: winners) }
                    if let nominees = award.nominees, !nominees.isEmpty { Text(tr("Номинанты", "Nominees")).font(.title2.bold()); PosterGrid(items: nominees) }
                    if award.winners == nil { PosterGrid(items: award.items) }
                } else if let list = model.list { if list.items.isEmpty { ContentUnavailableView(tr("Подборка пока недоступна", "Collection unavailable"), systemImage: "rectangle.stack") } else { PosterGrid(items: list.items, wide: list.items.allSatisfy { $0.section == "games" } && id != "top-rated") } }
                else { SkeletonGrid() }
                if id.hasPrefix("steam-shelf:"), let list = model.list {
                    HStack { Spacer(); Button(tr("Назад", "Previous")) { page = max(1, page - 1) }.disabled(page == 1); Text(String(page)); Button(tr("Далее", "Next")) { page += 1 }.disabled(!list.hasNext); Spacer() }
                }
            }.padding(24)
        }.task(id: id + String(page)) { await model.loadCollection(id, page: page); if isAward { await loadAwards() } }.task(id: year) { if isAward { await loadAwards() } }
    }
    private func loadAwards() async { do { let value = try await model.bridge.call("awards", ["award": .string(String(id.dropFirst(6))), "year": .number(Double(year))], as: AwardPage.self); if !Task.isCancelled { award = value } } catch is CancellationError {} catch { if !Task.isCancelled { model.error = error.localizedDescription } } }
}
