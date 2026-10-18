import SwiftUI

struct GameShelfList: Decodable { var items: [GameShelfDefinition] }
struct GameShelfDefinition: Decodable, Identifiable { var id: String; var title: String }
struct GameHomeView: View {
    @EnvironmentObject private var model: AppModel
    @State private var shelves: [GameShelfDefinition] = []
    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 28) {
                HStack { Text(tr("Игры", "Games")).font(.largeTitle.bold()); Spacer(); Button(tr("Все игры", "All games")) { model.filter.genre = "all" } }
                Label(tr("Каталог игр для Windows. Можно скачать здесь или передать загрузку на ПК.", "Windows game catalog. Download here or send a download to your PC."), systemImage: "desktopcomputer").font(.callout).foregroundStyle(.secondary)
                ForEach(shelves) { definition in GameShelfView(definition: definition) }
                if shelves.isEmpty { SkeletonGrid(wide: true) }
            }.padding(24)
        }.task(id: model.revision) {
            do { shelves = try await model.bridge.call("gameShelves", as: GameShelfList.self).items }
            catch is CancellationError {} catch { model.error = error.localizedDescription }
        }
    }
}
struct GameShelfView: View {
    @EnvironmentObject private var model: AppModel
    var definition: GameShelfDefinition
    @State private var result: CatalogResult?
    @State private var error: String?
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack { Text(definition.title).font(.title2.bold()); Spacer(); Button(tr("Ещё", "More")) { model.openCollection("steam-shelf:" + definition.id) } }
            if let result {
                if definition.id == "popular", let first = result.items.first { FeaturedHero(card: first) }
                ScrollView(.horizontal) { LazyHStack(alignment: .top, spacing: 16) { ForEach(model.visible(Array(result.items.prefix(12)))) { card in PosterCard(card: card, wide: true).frame(width: 230) } } }.scrollIndicators(.hidden)
            } else if let error { Text(error).font(.caption).foregroundStyle(.secondary); Button(tr("Повторить", "Retry")) { Task { await load() } } }
            else { HStack { ForEach(0..<3) { _ in RoundedRectangle(cornerRadius: 12).fill(.quaternary).frame(width: 230, height: 145) } }.clipped() }
        }.task(id: model.revision) { await load() }
    }
    private func load() async {
        do { result = try await model.bridge.call("gameShelf", ["id": .string(definition.id)], as: CatalogResult.self); error = nil }
        catch is CancellationError {} catch { self.error = error.localizedDescription }
    }
}
