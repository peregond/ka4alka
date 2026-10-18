import AppKit
import SwiftUI

struct DetailView: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard
    @State private var studio = ""
    @State private var audio = ""
    @State private var quality = ""
    @State private var releaseSort = "seeds"
    @State private var language = ""
    private var detail: DetailResult? { model.detail?.item.id == card.id ? model.detail : nil }
    private var item: MediaCard { detail?.item ?? card }
    private var visibleReleases: [ReleaseRow] {
        let rows = model.releases.filter { (studio.isEmpty || $0.studios.contains(studio)) && (audio.isEmpty || $0.audioTypes.contains(audio)) && (quality.isEmpty || $0.quality == quality) && (language.isEmpty || $0.title.localizedCaseInsensitiveContains(language)) }
        return releaseSort == "size" ? rows.sorted { ($0.size ?? 0) < ($1.size ?? 0) } : rows.sorted { ($0.seeds ?? -1) > ($1.seeds ?? -1) }
    }
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                ZStack(alignment: .bottomLeading) {
                    if let backdrop = detail?.backdrop { GeometryReader { geo in Artwork(address: backdrop, width: 1280).frame(width: geo.size.width, height: 320) } }
                    else { LinearGradient(colors: [model.accent.opacity(0.12), Color.primary.opacity(0.03)], startPoint: .topTrailing, endPoint: .bottomLeading) }
                    LinearGradient(colors: [.clear, .black.opacity(0.85)], startPoint: .top, endPoint: .bottom)
                    HStack(alignment: .bottom, spacing: 22) {
                        Artwork(address: item.poster).aspectRatio(2.0 / 3, contentMode: .fit).frame(width: 135).clipShape(RoundedRectangle(cornerRadius: 12))
                        VStack(alignment: .leading, spacing: 12) {
                            Text(item.title).font(.largeTitle.bold()).lineLimit(3)
                            Text([item.year > 0 ? String(item.year) : "", item.genre, item.rating == "—" ? "" : "★ " + item.rating].filter { !$0.isEmpty }.joined(separator: " · ")).foregroundStyle(.white.opacity(0.8))
                            HStack { Button { Task { await model.save(item) } } label: { Label(model.isSaved(item) ? tr("Сохранено", "Saved") : tr("Сохранить", "Save"), systemImage: model.isSaved(item) ? "heart.fill" : "heart") }; Button { Task { await model.hide(item); model.back() } } label: { Label(tr("Не интересно", "Not interested"), systemImage: "eye.slash") } }.buttonStyle(.bordered)
                        }.frame(maxWidth: .infinity, alignment: .leading)
                    }.padding(24).foregroundStyle(.white)
                }.frame(height: 320).clipShape(RoundedRectangle(cornerRadius: 18))
                if !item.description.isEmpty { Text(item.description).font(.body).lineSpacing(4).textSelection(.enabled) }
                else if model.detail == nil { ProgressView(tr("Загружаем описание…", "Loading description…")).controlSize(.small) }
                if let game = detail?.game {
                    Label(tr("Раздачи для Windows не запускаются в macOS без совместимого ПО.", "Windows releases do not run directly on macOS."), systemImage: "desktopcomputer").font(.callout).foregroundStyle(.secondary)
                    Text([game.developers, game.publishers, game.price].filter { !$0.isEmpty }.joined(separator: " · ")).font(.callout)
                    if !game.gallery.isEmpty { ScrollView(.horizontal) { LazyHStack { ForEach(game.gallery) { shot in Artwork(address: shot.thumb, width: 640).frame(width: 280, height: 157).clipShape(RoundedRectangle(cornerRadius: 9)).onTapGesture { if let url = URL(string: shot.full) { NSWorkspace.shared.open(url) } } } } }.scrollIndicators(.hidden) }
                    if !game.requirements.isEmpty { DisclosureGroup(tr("Системные требования", "System requirements")) { Text(game.requirements).font(.callout).frame(maxWidth: .infinity, alignment: .leading).padding(.top, 8) } }
                }
                if let people = detail?.people, !people.isEmpty {
                    Text(tr("Актёры и создатели", "Cast and crew")).font(.title2.bold())
                    ScrollView(.horizontal) { LazyHStack(alignment: .top, spacing: 16) { ForEach(people) { person in Button { model.open(person) } label: { VStack(alignment: .leading, spacing: 6) { Artwork(address: person.photoUrl, width: 240).frame(width: 100, height: 125).clipShape(RoundedRectangle(cornerRadius: 10)); Text(person.name).font(.callout.weight(.medium)).lineLimit(2); Text(tr(person.role)).font(.caption).foregroundStyle(.secondary) }.frame(width: 100) }.buttonStyle(.plain) } } }.scrollIndicators(.hidden)
                }
                if let awards = detail?.awards, !awards.isEmpty { DisclosureGroup(tr("Награды", "Awards")) { VStack(alignment: .leading, spacing: 8) { ForEach(awards) { award in Label("\(award.name) · \(award.year) · \(award.category)", systemImage: award.winner ? "trophy.fill" : "laurel.leading").font(.callout) } }.frame(maxWidth: .infinity, alignment: .leading).padding(.top, 8) } }
                releaseSection
            }.padding(24)
        }.task(id: card.id) { await model.loadDetail(card) }.background { Color.clear.task(id: card.id) { await model.loadReleases(card) } }
    }
    private var releaseSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack { Text(tr("Выбрать раздачу", "Choose a release")).font(.title2.bold()); if model.releasesLoading { ProgressView().controlSize(.small) }; Spacer(); Button { Task { await model.loadReleases(card) } } label: { Image(systemName: "arrow.clockwise") } }
            ScrollView(.horizontal) { HStack {
                releaseMenu(tr("Студия", "Studio"), value: $studio, options: Array(Set(model.releases.flatMap(\.studios))).sorted())
                releaseMenu(tr("Озвучка", "Audio"), value: $audio, options: Array(Set(model.releases.flatMap(\.audioTypes))).sorted())
                releaseMenu(tr("Качество", "Quality"), value: $quality, options: ["HD Ready", "Full HD", "4K"])
                Menu { Button(tr("Больше отдающих", "Most seeders")) { releaseSort = "seeds" }; Button(tr("Меньше размер", "Smallest size")) { releaseSort = "size" } } label: { Text(tr("Порядок", "Sort")) }
                Menu { Button(tr("Все языки", "All languages")) { language = "" }; Button("English") { language = "ENG" }; Button(tr("Русский", "Russian")) { language = "RUS" } } label: { Text(language.isEmpty ? tr("Язык", "Language") : language) }
                Toggle(tr("Без плохого качества", "Hide low quality"), isOn: Binding(get: { model.settings?.hidePoorQuality ?? true }, set: { next in Task { await model.set("hidePoorQuality", .bool(next)); await model.loadReleases(card) } })).toggleStyle(.button)
                if !studio.isEmpty || !audio.isEmpty || !quality.isEmpty || !language.isEmpty { Button(tr("Сбросить", "Reset")) { studio = ""; audio = ""; quality = ""; language = "" } }
            } }.scrollIndicators(.hidden)
            if !model.sources.isEmpty { Text(model.sources.map { $0.name + " · " + $0.state }.joined(separator: "    ")).font(.caption).foregroundStyle(.secondary).lineLimit(3) }
            if visibleReleases.isEmpty { if model.releasesLoading { ProgressView(tr("Ищем раздачи…", "Searching for releases…")).padding(.vertical, 24) } else { ContentUnavailableView(tr("Раздачи не найдены", "No releases found"), systemImage: "tray", description: Text(tr("Попробуй сбросить фильтры или повторить поиск позже.", "Reset filters or try again later."))) } }
            LazyVStack(spacing: 8) {
                ForEach(visibleReleases) { release in
                    ReleaseListRow(release: release, card: card)
                }
            }
        }
    }
    private func releaseMenu(_ title: String, value: Binding<String>, options: [String]) -> some View { Menu { Button(tr("Все", "All")) { value.wrappedValue = "" }; ForEach(options, id: \.self) { option in Button(tr(option)) { value.wrappedValue = option } } } label: { Text(value.wrappedValue.isEmpty ? title : tr(value.wrappedValue)) } }
}
struct PersonView: View {
    @EnvironmentObject var model: AppModel
    var person: Person
    var body: some View {
        ScrollView { VStack(alignment: .leading, spacing: 22) {
            HStack(alignment: .top, spacing: 22) { Artwork(address: person.photoUrl, width: 400).frame(width: 170, height: 215).clipShape(RoundedRectangle(cornerRadius: 14)); VStack(alignment: .leading, spacing: 14) { Text(person.name).font(.largeTitle.bold()); Text(tr(person.role)).foregroundStyle(.secondary); if let profile = model.person { Text(profile.description).textSelection(.enabled) } else { ProgressView(tr("Загружаем биографию…", "Loading biography…")) } }.frame(maxWidth: .infinity, alignment: .leading) }
            if let profile = model.person { Text(tr("Фильмография", "Filmography")).font(.title2.bold()); PosterGrid(items: profile.items) }
        }.padding(24) }.task(id: person.id) { await model.loadPerson(person) }
    }
}

struct ReleaseListRow: View {
    @EnvironmentObject private var model: AppModel
    var release: ReleaseRow
    var card: MediaCard
    private var metadata: String {
        [release.source, release.poor ? tr("Экранка", "Low quality") : release.quality,
         release.audio, release.size.map(byteText) ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
    }
    var body: some View {
        HStack(spacing: 14) {
            VStack(alignment: .leading, spacing: 6) {
                Text(release.title).font(.callout.weight(.medium)).lineLimit(3)
                Text(metadata).font(.caption).foregroundStyle(.secondary)
                Text(tr("Отдают", "Seeders") + ": " + (release.seeds.map(String.init) ?? "—") + "   " + tr("Скачивают", "Leechers") + ": " + (release.leechers.map(String.init) ?? "—"))
                    .font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }.frame(maxWidth: .infinity, alignment: .leading)
            Menu {
                Button(tr("Скачать на этот Mac", "Download on this Mac")) {
                    Task { await model.add(release, card: card) }
                }
                ForEach((model.lanState?.devices ?? []).filter(\.paired)) { device in
                    Button(tr("Скачать на ", "Download on ") + device.name) {
                        Task { await model.lan("send", ["device": .string(device.id), "release": .string(release.id)]) }
                    }
                }
            } label: {
                Label(tr("Скачать", "Download"), systemImage: "arrow.down")
            }.disabled(!release.downloadable)
        }.padding(14).background(Color.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 12))
    }
}
