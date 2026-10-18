import SwiftUI

struct PosterCard: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard; var wide = false; var rank: Int? = nil
    @State private var hovered = false
    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            ZStack(alignment: .topLeading) {
                Button { model.open(card) } label: {
                    GeometryReader { geo in Artwork(address: card.poster).frame(width: geo.size.width, height: geo.size.height) }.aspectRatio(wide ? 460.0 / 215 : 2.0 / 3, contentMode: .fit)
                }.buttonStyle(.plain)
                if let quality = card.quality { Text(quality).font(.caption2.bold()).padding(.horizontal, 7).padding(.vertical, 4).background(.black.opacity(0.65), in: Capsule()).foregroundStyle(.white).padding(8).allowsHitTesting(false) }
                VStack { Spacer(); HStack { if card.rating != "—", !card.rating.isEmpty { Label(card.rating, systemImage: "star.fill").font(.caption.bold()).foregroundStyle(.yellow).padding(5).background(.black.opacity(0.75), in: RoundedRectangle(cornerRadius: 6)) }; Spacer(); if let rank { Text(String(rank)).font(.system(size: 40, weight: .heavy, design: .rounded)).foregroundStyle(.white.opacity(0.9)) } }.padding(8) }.allowsHitTesting(false)
                VStack { HStack { Spacer(); Button { Task { await model.save(card) } } label: { Image(systemName: model.isSaved(card) ? "heart.fill" : "heart") }.help(tr("Сохранить", "Save")); Button { Task { await model.hide(card) } } label: { Image(systemName: "eye.slash") }.help(tr("Не интересно", "Not interested")) }.buttonStyle(.bordered).controlSize(.small); Spacer() }.padding(7).opacity(hovered ? 1 : 0)
            }.clipShape(RoundedRectangle(cornerRadius: 12)).onHover { hovered = $0 }
            Button { model.open(card) } label: { Text(card.title).font(.body.weight(.semibold)).lineLimit(2).frame(maxWidth: .infinity, minHeight: 36, alignment: .topLeading) }.buttonStyle(.plain)
            Text([card.year > 0 ? String(card.year) : "", card.genre].filter { !$0.isEmpty }.joined(separator: " · ")).font(.caption).foregroundStyle(.secondary).lineLimit(1)
        }.contextMenu {
            Button(model.isSaved(card) ? tr("Удалить из сохранённого", "Remove from Saved") : tr("Сохранить", "Save")) { Task { await model.save(card) } }
            Button(tr("Не интересно", "Not interested")) { Task { await model.hide(card) } }
        }.accessibilityElement(children: .contain).accessibilityLabel(card.title)
    }
}
struct PosterGrid: View {
    @EnvironmentObject var model: AppModel
    var items: [MediaCard]; var wide = false
    var body: some View { LazyVGrid(columns: [GridItem(.adaptive(minimum: wide ? 220 : 145, maximum: wide ? 360 : 220), spacing: 16)], alignment: .leading, spacing: 22) { ForEach(model.visible(items)) { card in PosterCard(card: card, wide: wide).id(card.id) } }.scrollTargetLayout() }
}
struct CatalogView: View {
    @EnvironmentObject private var model: AppModel
    var saved = false
    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(saved ? tr("Сохранённое", "Saved") : model.submittedQuery.isEmpty ? model.section.title : tr("Результаты поиска", "Search results")).font(.title.bold()); Spacer()
                if model.loading { ProgressView().controlSize(.small) }
                Button { model.filter = CatalogFilter(); model.query = ""; model.submittedQuery = ""; model.page = 1; model.revision += 1 } label: { Image(systemName: "arrow.uturn.backward") }.help(tr("Сбросить фильтры", "Reset filters")).disabled(!model.filter.active && model.submittedQuery.isEmpty)
                Button { Task { await model.refresh() } } label: { Image(systemName: "arrow.clockwise") }.help(tr("Обновить каталог", "Refresh catalog"))
            }.padding([.top, .horizontal], 22).padding(.bottom, 12)
            if !saved { FilterBar().padding(.horizontal, 22).padding(.bottom, 14) }
            if model.section == .games, !saved { Label(tr("Каталог игр для Windows. Загрузки можно передать на ПК по локальной сети.", "Windows game catalog. Send downloads to a PC on your local network."), systemImage: "desktopcomputer").font(.caption).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 22).padding(.bottom, 10) }
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if let result = model.list {
                        if result.items.isEmpty { ContentUnavailableView.search(text: model.submittedQuery) }
                        else { PosterGrid(items: result.items, wide: model.section == .games && !saved) }
                        if !saved, model.submittedQuery.isEmpty {
                            HStack { Spacer(); Button(tr("Назад", "Previous")) { model.page = max(1, model.page - 1); model.browseAnchor = nil }.disabled(model.page <= 1); Text(String(model.page)).monospacedDigit().padding(.horizontal, 12); Button(tr("Далее", "Next")) { model.page += 1; model.browseAnchor = nil }.disabled(!result.hasNext); Spacer() }.padding(.vertical, 18)
                        }
                        if result.offline == true { Label(tr("Показаны сохранённые результаты. Часть источников недоступна.", "Showing cached results. Some sources are unavailable."), systemImage: "wifi.slash").font(.caption).foregroundStyle(.secondary) }
                    } else { SkeletonGrid(wide: model.section == .games) }
                }.padding(.horizontal, 22).padding(.bottom, 22)
            }.scrollPosition(id: Binding(get: { model.browseAnchor }, set: { model.browseAnchor = $0 }))
        }.task(id: saved ? "saved-\(model.revision)" : model.requestKey) { if saved { await model.loadSaved() } else { await model.loadCatalog() } }
    }
}
struct FilterBar: View {
    @EnvironmentObject var model: AppModel
    var body: some View {
        ScrollView(.horizontal) {
            HStack(spacing: 10) {
                if !model.submittedQuery.isEmpty, model.section != .games {
                    Picker(tr("Показывать", "Show"), selection: $model.filter.kind) { Text(tr("Все", "All")).tag(""); Text(tr("Фильмы", "Movies")).tag("movies"); Text(tr("Сериалы", "Series")).tag("series") }.pickerStyle(.segmented).frame(width: 240)
                }
                choice(tr("Жанр", "Genre"), value: $model.filter.genre, items: model.section == .games ? model.gameGenres : model.genres)
                if model.section != .games {
                    choice(tr("Страна", "Country"), value: $model.filter.country, items: model.countries)
                    Menu { Button(tr("Любой год", "Any year")) { model.filter.year = 0 }; ForEach((1950...(Calendar.current.component(.year, from: Date()))).reversed(), id: \.self) { year in Button(String(year)) { model.filter.year = year } } } label: { Text(model.filter.year == 0 ? tr("Год", "Year") : String(model.filter.year)) }
                    Menu { Button(tr("Любой рейтинг", "Any rating")) { model.filter.rating = 0 }; ForEach((1...9).reversed(), id: \.self) { value in Button("\(value)+") { model.filter.rating = value } } } label: { Text(model.filter.rating == 0 ? tr("Рейтинг", "Rating") : "\(model.filter.rating)+") }
                    Menu { ForEach(["", "HD Ready", "Full HD", "4K"], id: \.self) { value in Button(value.isEmpty ? tr("Любое качество", "Any quality") : value) { model.filter.quality = value } }; Divider(); Toggle(tr("Без плохого качества", "Hide low quality"), isOn: Binding(get: { model.settings?.hidePoorQuality ?? true }, set: { value in Task { await model.set("hidePoorQuality", .bool(value)) } })) } label: { Text(model.filter.quality.isEmpty ? tr("Качество", "Quality") : model.filter.quality) }
                }
                Menu {
                    if model.section == .games { ForEach([("popular", tr("Популярное", "Popular")), ("new", tr("Новинки", "New")), ("specials", tr("Скидки", "On sale")), ("soon", tr("Скоро выйдут", "Coming soon"))], id: \.0) { key, text in Button(text) { model.filter.sort = key } } }
                    else { Button(tr("Сначала новые", "Newest first")) { model.filter.sort = "date" }; Button(tr("По рейтингу", "Top rated")) { model.filter.sort = "rating" } }
                } label: { Label(tr("Порядок", "Sort"), systemImage: "line.3.horizontal.decrease") }
            }.controlSize(.regular)
        }.scrollIndicators(.hidden).onChange(of: model.filter) { _, _ in model.page = 1; model.browseAnchor = nil }
    }
    private func choice(_ title: String, value: Binding<String>, items: [Choice]) -> some View {
        Menu { Button(tr("Все", "All")) { value.wrappedValue = "" }; ForEach(items) { item in Button(item.label) { value.wrappedValue = item.key } } } label: { Text(items.first { $0.key == value.wrappedValue }?.label ?? title) }
    }
}
struct SkeletonGrid: View {
    var wide = false
    var body: some View { LazyVGrid(columns: [GridItem(.adaptive(minimum: wide ? 220 : 145, maximum: wide ? 360 : 220), spacing: 16)], spacing: 22) { ForEach(0..<12) { _ in VStack(alignment: .leading, spacing: 10) { RoundedRectangle(cornerRadius: 12).fill(.quaternary).aspectRatio(wide ? 460.0 / 215 : 2.0 / 3, contentMode: .fit); RoundedRectangle(cornerRadius: 4).fill(.quaternary).frame(height: 14); RoundedRectangle(cornerRadius: 4).fill(.quaternary).frame(width: 80, height: 11) }.accessibilityHidden(true) } } }
}
