import AppKit
import SwiftUI

// MARK: Text helpers

enum DetailReleaseText {
    /// The engine writes "Не указано" for facts it could not read from a title. Those are never shown.
    static func meaningful(_ text: String) -> Bool {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return !trimmed.isEmpty && trimmed != "Не указано" && trimmed != "—"
    }

    static func qualityRank(_ quality: String) -> Int {
        switch quality {
        case "4K": return 4
        case "Full HD": return 3
        case "HD Ready": return 2
        case "SD": return 1
        default: return 0
        }
    }

    /// The big label on the left of a row: "4K", "Full HD", "Экранка", or the game release kind.
    static func qualityText(_ release: ReleaseRow, isGame: Bool) -> String {
        if isGame {
            if let kind = release.gameKind, meaningful(kind) { return kind }
            return "—"
        }
        if let label = release.qualityLabel, meaningful(label) { return label }
        if meaningful(release.quality) { return release.quality }
        return "—"
    }
}

// MARK: Source states

/// Words for the engine's real source states. An unknown value is never shown raw.
func sourceState(_ state: String) -> String {
    switch state {
    case "Searching": return tr("Поиск…", "Searching…")
    case "Ready": return tr("Найдено", "Found")
    case "Empty": return tr("Нет результатов", "No results")
    case "Unavailable": return tr("Недоступен", "Unavailable")
    case "TimedOut": return tr("Нет ответа", "No response")
    case "Indexed": return tr("В индексе", "Indexed")
    case "Saved": return tr("Сохранённые", "Saved")
    default: return ""
    }
}

/// "Найдено: 12" when the engine reports a count, otherwise the plain state.
func sourceStateDescription(_ source: SourceState) -> String {
    let text = sourceState(source.state)
    if source.state == "Ready", let count = source.count, count > 0 { return text + ": " + String(count) }
    return text
}

func sourceStateTone(_ state: String) -> KDTone {
    switch state {
    case "Ready", "Indexed", "Saved": return .success
    case "Searching": return .info
    case "Unavailable", "TimedOut": return .warning
    default: return .neutral
    }
}

func sourceStateFailed(_ state: String) -> Bool { state == "Unavailable" || state == "TimedOut" }

// MARK: Filter state

enum DetailReleaseSort: String, CaseIterable, Identifiable {
    case recommended, seeds, size, quality, title
    var id: String { rawValue }
    var title: String {
        switch self {
        case .recommended: return tr("Лучшие", "Best")
        case .seeds: return tr("Больше раздающих", "Most seeders")
        case .size: return tr("Меньший размер", "Smallest size")
        case .quality: return tr("Качество", "Quality")
        case .title: return tr("Название", "Title")
        }
    }
}

struct DetailReleaseFilters: Equatable {
    var studio = ""
    var audio = ""
    var quality = ""
    var russianOnly = false
    var codec = ""
    var hdr = ""
    var subtitles = false
    var source = ""
    var season = 0
    var episode = 0
    var language = ""
    var sort: DetailReleaseSort = .recommended

    /// How many of the filters behind "Ещё фильтры" are set.
    var extraCount: Int {
        var count = 0
        if !codec.isEmpty { count += 1 }
        if !hdr.isEmpty { count += 1 }
        if subtitles { count += 1 }
        if !source.isEmpty { count += 1 }
        if season != 0 { count += 1 }
        if episode != 0 { count += 1 }
        if !language.isEmpty { count += 1 }
        return count
    }

    var isDefault: Bool { self == DetailReleaseFilters() }

    /// True when a filter (not just the sort order) hides some rows.
    var hidesRows: Bool {
        var copy = self
        copy.sort = .recommended
        return copy != DetailReleaseFilters()
    }

    func matches(_ release: ReleaseRow) -> Bool {
        if !studio.isEmpty, !(release.studios.contains(studio) || release.gameGroup == studio) { return false }
        if !audio.isEmpty, !release.audioTypes.contains(audio) { return false }
        if !quality.isEmpty, release.quality != quality { return false }
        if russianOnly, !(release.russianDub ?? release.russianAudio) { return false }
        if !codec.isEmpty, release.codec != codec { return false }
        if !hdr.isEmpty, release.hdr != hdr { return false }
        if subtitles, !DetailReleaseText.meaningful(release.subs) { return false }
        if !source.isEmpty, release.source != source { return false }
        if season != 0, !(release.multipleSeasons || release.season == season) { return false }
        if episode != 0, !(release.multipleEpisodes || release.episode == episode) { return false }
        if !language.isEmpty {
            let spoken: Bool = language == "ENG" ? release.englishAudio : release.russianAudio
            if !spoken { return false }
        }
        return true
    }

    /// Filters, then orders. Equal rows keep the order the engine gave them.
    func arrange(_ rows: [ReleaseRow]) -> [ReleaseRow] {
        let kept: [ReleaseRow] = rows.filter { matches($0) }
        let indexed: [(offset: Int, element: ReleaseRow)] = Array(kept.enumerated())
        let ordered = indexed.sorted { (left: (offset: Int, element: ReleaseRow), right: (offset: Int, element: ReleaseRow)) -> Bool in
            if let result = precedes(left.element, right.element) { return result }
            return left.offset < right.offset
        }
        return ordered.map { $0.element }
    }

    /// nil when the two rows are equal for the current sort order.
    private func precedes(_ a: ReleaseRow, _ b: ReleaseRow) -> Bool? {
        switch sort {
        case .recommended:
            if a.recommended != b.recommended { return a.recommended }
            return nil
        case .seeds:
            let x: Int = a.seeds ?? -1
            let y: Int = b.seeds ?? -1
            if x == y { return nil }
            return x > y
        case .size:
            let x: Double = a.size ?? Double.greatestFiniteMagnitude
            let y: Double = b.size ?? Double.greatestFiniteMagnitude
            if x == y { return nil }
            return x < y
        case .quality:
            let x: Int = a.poor ? 0 : DetailReleaseText.qualityRank(a.quality)
            let y: Int = b.poor ? 0 : DetailReleaseText.qualityRank(b.quality)
            if x == y { return nil }
            return x > y
        case .title:
            let order = a.title.localizedStandardCompare(b.title)
            if order == .orderedSame { return nil }
            return order == .orderedAscending
        }
    }
}

struct DetailFacetCount: Identifiable, Equatable {
    var value: String
    var count: Int
    var id: String { value }
}

/// What the loaded rows offer: used to build the menus and to hide menus with nothing to choose.
struct DetailReleaseFacets {
    var total = 0
    var qualities: [DetailFacetCount] = []
    var studios: [String] = []
    var audioTypes: [String] = []
    var codecs: [String] = []
    var hdrs: [String] = []
    var sources: [String] = []
    var seasons: [Int] = []
    var episodes: [Int] = []
    var hasRussian = false
    var hasEnglish = false
    var hasSubtitles = false

    init(rows: [ReleaseRow], season: Int) {
        total = rows.count
        var counts: [String: Int] = [:]
        var studioSet = Set<String>()
        var audioSet = Set<String>()
        var codecSet = Set<String>()
        var hdrSet = Set<String>()
        var sourceSet = Set<String>()
        var seasonSet = Set<Int>()
        var episodeSet = Set<Int>()
        for row in rows {
            if DetailReleaseText.meaningful(row.quality) { counts[row.quality, default: 0] += 1 }
            for name in row.studios where DetailReleaseText.meaningful(name) { studioSet.insert(name) }
            for name in row.audioTypes where DetailReleaseText.meaningful(name) { audioSet.insert(name) }
            if DetailReleaseText.meaningful(row.codec) { codecSet.insert(row.codec) }
            if DetailReleaseText.meaningful(row.hdr) { hdrSet.insert(row.hdr) }
            if DetailReleaseText.meaningful(row.source) { sourceSet.insert(row.source) }
            if let value = row.season { seasonSet.insert(value) }
            if let value = row.episode, season == 0 || row.season == season || row.multipleSeasons { episodeSet.insert(value) }
            if row.russianAudio { hasRussian = true }
            if row.englishAudio { hasEnglish = true }
            if DetailReleaseText.meaningful(row.subs) { hasSubtitles = true }
        }
        let entries: [DetailFacetCount] = counts.map { DetailFacetCount(value: $0.key, count: $0.value) }
        qualities = entries.sorted { (left: DetailFacetCount, right: DetailFacetCount) -> Bool in
            let a = DetailReleaseText.qualityRank(left.value)
            let b = DetailReleaseText.qualityRank(right.value)
            if a != b { return a > b }
            return left.value < right.value
        }
        studios = studioSet.sorted()
        audioTypes = audioSet.sorted()
        codecs = codecSet.sorted()
        hdrs = hdrSet.sorted()
        sources = sourceSet.sorted()
        seasons = seasonSet.sorted()
        episodes = episodeSet.sorted()
    }
}

// MARK: Filter bar

struct ReleaseFilterBar: View {
    @EnvironmentObject private var model: AppModel
    @Binding var filters: DetailReleaseFilters
    @Binding var showsExtras: Bool
    var facets: DetailReleaseFacets
    var card: MediaCard
    var isGame: Bool
    var isSeries: Bool

    private var showsQualityPicker: Bool { !isGame && facets.qualities.count >= 2 }
    private var showsStudio: Bool { !isGame && (facets.studios.count >= 2 || !filters.studio.isEmpty) }
    private var showsAudio: Bool { !isGame && (facets.audioTypes.count >= 2 || !filters.audio.isEmpty) }
    private var showsRussianToggle: Bool { !isGame && (facets.hasRussian || filters.russianOnly) }
    private var showsCodec: Bool { !isGame && (facets.codecs.count >= 2 || !filters.codec.isEmpty) }
    private var showsHDR: Bool { !isGame && (facets.hdrs.count >= 2 || !filters.hdr.isEmpty) }
    private var showsSubtitles: Bool { !isGame && (facets.hasSubtitles || filters.subtitles) }
    private var showsSource: Bool { facets.sources.count >= 2 || !filters.source.isEmpty }
    private var showsSeason: Bool { isSeries && (facets.seasons.count >= 2 || filters.season != 0) }
    private var showsEpisode: Bool { isSeries && (facets.episodes.count >= 2 || filters.episode != 0) }
    private var showsLanguage: Bool { !isGame && ((facets.hasEnglish && facets.hasRussian) || !filters.language.isEmpty) }
    private var hasExtras: Bool { !isGame || showsSource }
    private var extrasTitle: String {
        let base = tr("Ещё фильтры", "More filters")
        return filters.extraCount > 0 ? base + " · " + String(filters.extraCount) : base
    }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
                if showsQualityPicker { qualityPicker }
                if showsStudio { stringMenu(tr("Студия", "Studio"), selection: $filters.studio, options: facets.studios) }
                if showsAudio { stringMenu(tr("Тип озвучки", "Voice type"), selection: $filters.audio, options: facets.audioTypes) }
                if showsRussianToggle { russianToggle }
                sortMenu
                if hasExtras { extrasButton }
                if !filters.isDefault { resetButton }
            }
            if showsExtras && hasExtras { extraPanel }
        }
    }

    // MARK: Primary controls

    private var qualityPicker: some View {
        Picker(tr("Качество", "Quality"), selection: $filters.quality) {
            Text(tr("Все", "All") + " " + String(facets.total)).tag("")
            ForEach(facets.qualities) { entry in
                Text(tr(entry.value) + " " + String(entry.count)).tag(entry.value)
            }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .fixedSize()
    }

    private var russianToggle: some View {
        Toggle(tr("Только русская озвучка", "Russian audio only"), isOn: $filters.russianOnly)
            .toggleStyle(.switch)
            .controlSize(.small)
            .fixedSize()
    }

    private var sortMenu: some View {
        let text = tr("Порядок: ", "Sort: ") + filters.sort.title
        let value: String? = filters.sort == .recommended ? nil : text
        return KDChipMenu(title: text, value: value) {
            Picker(tr("Порядок", "Sort"), selection: $filters.sort) {
                ForEach(DetailReleaseSort.allCases) { option in
                    Text(option.title).tag(option)
                }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
    }

    private var extrasButton: some View {
        Button {
            showsExtras.toggle()
        } label: {
            KDChipLabel(text: extrasTitle, systemImage: "slider.horizontal.3", isActive: filters.extraCount > 0, showsChevron: true)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(tr("Ещё фильтры", "More filters"))
        .accessibilityValue(showsExtras ? tr("Раскрыто", "Expanded") : tr("Свёрнуто", "Collapsed"))
    }

    private var resetButton: some View {
        Button(tr("Сбросить", "Reset")) { filters = DetailReleaseFilters() }
            .buttonStyle(.plain)
            .font(.callout.weight(.semibold))
            .foregroundStyle(KDPalette.accent)
            .padding(.vertical, 4)
    }

    // MARK: Extra filters

    private var seasonBinding: Binding<Int> {
        Binding<Int>(
            get: { self.filters.season },
            set: { value in
                self.filters.season = value
                self.filters.episode = 0
            }
        )
    }

    private var poorQualityBinding: Binding<Bool> {
        Binding<Bool>(
            get: { self.model.settings?.hidePoorQuality ?? true },
            set: { next in
                Task { @MainActor in
                    await self.model.set("hidePoorQuality", .bool(next))
                    await self.model.loadReleases(self.card)
                }
            }
        )
    }

    private var extraPanel: some View {
        KDFlowLayout(spacing: KD.Space.xs, lineSpacing: KD.Space.xs) {
            if showsCodec { stringMenu(tr("Видео", "Video"), selection: $filters.codec, options: facets.codecs) }
            if showsHDR { stringMenu("HDR", selection: $filters.hdr, options: facets.hdrs) }
            if showsSubtitles { subtitlesChip }
            if showsSource { stringMenu(tr("Источник", "Source"), selection: $filters.source, options: facets.sources) }
            if showsSeason { intMenu(tr("Сезон", "Season"), prefix: "S", selection: seasonBinding, options: facets.seasons, allTitle: tr("Все сезоны", "All seasons")) }
            if showsEpisode { intMenu(tr("Серия", "Episode"), prefix: "E", selection: $filters.episode, options: facets.episodes, allTitle: tr("Все серии", "All episodes")) }
            if showsLanguage { languageMenu }
            if !isGame { poorQualityToggle }
        }
        .padding(KD.Space.s)
        .frame(maxWidth: .infinity, alignment: .leading)
        .kdCard(radius: KD.Radius.control, level: .sunken)
    }

    private var poorQualityToggle: some View {
        Toggle(tr("Скрывать экранки и низкое качество", "Hide low quality"), isOn: poorQualityBinding)
            .toggleStyle(.switch)
            .controlSize(.small)
            .fixedSize()
    }

    private var subtitlesChip: some View {
        Button {
            filters.subtitles.toggle()
        } label: {
            KDChipLabel(text: tr("С субтитрами", "With subtitles"), systemImage: filters.subtitles ? "checkmark" : nil, isActive: filters.subtitles)
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(filters.subtitles ? .isSelected : [])
    }

    private var languageMenu: some View {
        let value: String? = filters.language.isEmpty ? nil : (filters.language == "ENG" ? "English" : tr("Русский", "Russian"))
        return KDChipMenu(title: tr("Язык", "Language"), value: value) {
            Picker(tr("Язык", "Language"), selection: $filters.language) {
                Text(tr("Все языки", "All languages")).tag("")
                Text("English").tag("ENG")
                Text(tr("Русский", "Russian")).tag("RUS")
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
    }

    // MARK: Menu builders

    private func stringMenu(_ title: String, selection: Binding<String>, options: [String]) -> some View {
        let value: String? = selection.wrappedValue.isEmpty ? nil : tr(selection.wrappedValue)
        return KDChipMenu(title: title, value: value) {
            Picker(title, selection: selection) {
                Text(tr("Все", "All")).tag("")
                ForEach(options, id: \.self) { option in
                    Text(tr(option)).tag(option)
                }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
    }

    private func intMenu(_ title: String, prefix: String, selection: Binding<Int>, options: [Int], allTitle: String) -> some View {
        let value: String? = selection.wrappedValue == 0 ? nil : prefix + String(selection.wrappedValue)
        return KDChipMenu(title: title, value: value) {
            Picker(title, selection: selection) {
                Text(allTitle).tag(0)
                ForEach(options, id: \.self) { option in
                    Text(String(option)).tag(option)
                }
            }
            .pickerStyle(.inline)
            .labelsHidden()
        }
    }
}

// MARK: Source status

struct SourceStatusView: View {
    var sources: [SourceState]
    @State private var expanded = false

    private var failed: Int { sources.filter { sourceStateFailed($0.state) }.count }
    private var summary: String {
        if failed == 0 { return tr("Источники · \(sources.count)", "Sources · \(sources.count)") }
        return tr("Не ответили: \(failed)", "No answer: \(failed)")
    }
    private var tone: KDTone { failed == 0 ? KDTone.success : KDTone.warning }

    var body: some View {
        if !sources.isEmpty {
            DisclosureGroup(isExpanded: $expanded) {
                statusGrid
            } label: {
                Label(summary, systemImage: failed == 0 ? "checkmark.circle" : "exclamationmark.triangle")
                    .font(.callout)
                    .foregroundStyle(tone.foreground)
            }
        }
    }

    private var statusGrid: some View {
        Grid(alignment: .leading, horizontalSpacing: KD.Space.m, verticalSpacing: 6) {
            ForEach(sources) { source in
                GridRow {
                    Text(source.name).font(.callout)
                    HStack(spacing: 6) {
                        Circle()
                            .fill(sourceStateTone(source.state).foreground)
                            .frame(width: 8, height: 8)
                            .accessibilityHidden(true)
                        Text(sourceStateDescription(source)).font(.callout).foregroundStyle(.secondary)
                    }
                }
            }
        }
        .padding(.top, 6)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// MARK: Release row

struct DetailReleaseTag: Identifiable {
    var id: String
    var text: String
    var tone: KDTone
    var symbol: String?
}

struct ReleaseListRow: View {
    @EnvironmentObject private var model: AppModel
    var release: ReleaseRow
    var card: MediaCard
    var transfer: Transfer?
    var devices: [LanDevice]
    @State private var hovered = false
    @State private var adding = false

    private var isGame: Bool { card.section == "games" }
    private var shape: RoundedRectangle { RoundedRectangle(cornerRadius: KD.Radius.control, style: .continuous) }

    private var reason: String {
        var parts: [String] = []
        let label = DetailReleaseText.qualityText(release, isGame: false)
        if label != "—" { parts.append(label) }
        if release.russianDub ?? release.russianAudio { parts.append(tr("русская озвучка", "Russian audio")) }
        if let seeds = release.seeds, seeds >= 50 { parts.append(tr("много раздающих", "many seeders")) }
        if parts.isEmpty { return tr("Рекомендуем", "Recommended") }
        return tr("Рекомендуем: ", "Recommended: ") + parts.joined(separator: ", ")
    }

    private var episodeNote: String {
        guard card.section == "series" else { return "" }
        var parts: [String] = []
        if release.multipleSeasons { parts.append(tr("Несколько сезонов", "Several seasons")) }
        else if let season = release.season { parts.append(tr("Сезон \(season)", "Season \(season)")) }
        if release.multipleEpisodes { parts.append(tr("Несколько серий", "Several episodes")) }
        else if let episode = release.episode { parts.append(tr("Серия \(episode)", "Episode \(episode)")) }
        return parts.joined(separator: " · ")
    }

    private var metaLine: String {
        var parts: [String] = []
        if DetailReleaseText.meaningful(release.source) { parts.append(release.source) }
        if !episodeNote.isEmpty { parts.append(episodeNote) }
        return parts.joined(separator: " · ")
    }

    private var tags: [DetailReleaseTag] {
        var result: [DetailReleaseTag] = []
        var seen = Set<String>()
        func add(_ text: String, _ tone: KDTone, _ symbol: String?) {
            let key = text.lowercased()
            guard DetailReleaseText.meaningful(text), !seen.contains(key) else { return }
            seen.insert(key)
            result.append(DetailReleaseTag(id: key, text: text, tone: tone, symbol: symbol))
        }
        if isGame {
            if let group = release.gameGroup { add(group, .accent, nil) }
            if let version = release.gameVersion { add(version, .neutral, nil) }
        } else {
            for studio in release.studios { add(studio, .neutral, nil) }
            for kind in release.audioTypes { add(tr(kind), .info, nil) }
            if release.studios.isEmpty && release.audioTypes.isEmpty { add(release.audio, .neutral, nil) }
            if DetailReleaseText.meaningful(release.subs) { add(tr("Субтитры", "Subtitles") + ": " + release.subs, .neutral, nil) }
            add(release.codec, .neutral, nil)
            add(release.hdr, .accent, nil)
        }
        if release.poor { add(tr("Низкое качество", "Low quality"), .warning, "exclamationmark.triangle") }
        return result
    }

    private var seedTone: KDTone {
        guard let seeds = release.seeds else { return KDTone.neutral }
        if seeds >= 50 { return KDTone.success }
        if seeds >= 10 { return KDTone.warning }
        return KDTone.danger
    }
    private var seedsText: String {
        guard let seeds = release.seeds else { return "—" }
        return "↑ " + String(seeds)
    }
    private var seedsHint: String? {
        guard let seeds = release.seeds else { return nil }
        if seeds == 0 { return tr("Нет раздающих", "No seeders") }
        if seeds < 10 { return tr("Мало отдающих", "Few seeders") }
        return nil
    }
    private var seedsSpoken: String {
        guard let seeds = release.seeds else { return tr("Раздающих неизвестно", "Seeders unknown") }
        return tr("Раздают: ", "Seeders: ") + String(seeds)
    }
    private var seedsHelp: String {
        let leechers: String = release.leechers.map { String($0) } ?? "—"
        return seedsSpoken + " · " + tr("Скачивают: ", "Leechers: ") + leechers
    }
    private var sizeText: String {
        guard let size = release.size else { return "—" }
        return KDFormat.bytes(size)
    }

    var body: some View {
        HStack(alignment: .top, spacing: KD.Space.m) {
            qualityColumn
            infoColumn
            VStack(alignment: .trailing, spacing: KD.Space.xs) {
                factsColumn
                actionView
            }
        }
        .padding(KD.Space.s)
        .background(rowBackground)
        .contentShape(shape)
        .onHover { hovered = $0 }
        .accessibilityElement(children: .contain)
    }

    @ViewBuilder private var rowBackground: some View {
        if release.recommended {
            shape.fill(KDPalette.accentSoft).overlay(shape.strokeBorder(KDPalette.accentLine, lineWidth: 1))
        } else {
            shape.fill(hovered ? Color.primary.opacity(0.07) : KDPalette.surface)
                .overlay(shape.strokeBorder(KDPalette.hairline, lineWidth: 1))
        }
    }

    private var qualityColumn: some View {
        Text(DetailReleaseText.qualityText(release, isGame: isGame))
            .font(Font.title3.weight(.semibold).monospacedDigit())
            .lineLimit(2)
            .minimumScaleFactor(0.7)
            .frame(width: 64, alignment: .leading)
    }

    private var infoColumn: some View {
        VStack(alignment: .leading, spacing: 6) {
            if release.recommended {
                Label(reason, systemImage: "checkmark.seal.fill")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(KDPalette.accent)
                    .lineLimit(1)
            }
            Text(release.title)
                .font(Font.callout.weight(.medium))
                .lineLimit(2)
                .textSelection(.enabled)
            if !metaLine.isEmpty {
                Text(metaLine).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            let items = tags
            if !items.isEmpty {
                KDFlowLayout(spacing: 6, lineSpacing: 6) {
                    ForEach(items) { tag in
                        KDStatusPill(tag.text, tone: tag.tone, systemImage: tag.symbol)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var factsColumn: some View {
        VStack(alignment: .trailing, spacing: 2) {
            Text(sizeText).font(Font.callout.monospacedDigit())
            Text(seedsText)
                .font(Font.callout.monospacedDigit())
                .foregroundStyle(seedTone.foreground)
                .help(seedsHelp)
                .accessibilityLabel(seedsSpoken)
            if let hint = seedsHint {
                Text(hint).font(.caption).foregroundStyle(seedTone.foreground)
            }
        }
    }

    // MARK: Action

    @ViewBuilder private var actionView: some View {
        if let transfer {
            queuedView(transfer)
        } else if adding {
            ProgressView().controlSize(.small).frame(minHeight: 24)
        } else if devices.isEmpty {
            singleDownload
        } else {
            deviceMenu
        }
    }

    private var noLinkHelp: String {
        release.downloadable ? "" : tr("У источника нет ссылки на торрент", "This source has no torrent link")
    }

    private var downloadTitle: String { tr("Скачать", "Download") }

    private var singleDownload: some View {
        Button {
            start()
        } label: {
            Label(downloadTitle, systemImage: "arrow.down")
        }
        .buttonStyle(KDPrimaryButtonStyle())
        .controlSize(.small)
        .disabled(!release.downloadable)
        .help(noLinkHelp)
        .accessibilityLabel(downloadTitle + ": " + release.title)
    }

    private var menuLabel: some View {
        Label(downloadTitle, systemImage: "arrow.down")
            .font(.callout.weight(.semibold))
            .foregroundStyle(KDPalette.accentInk)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(KDPalette.accentFill, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
    }

    private var deviceMenu: some View {
        Menu {
            Button(tr("Скачать на этот Mac", "Download on this Mac")) { start() }
            ForEach(devices) { device in
                Button(tr("Скачать на ", "Download on ") + device.name) { send(to: device) }
            }
        } label: {
            menuLabel
        } primaryAction: {
            start()
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .disabled(!release.downloadable)
        .help(noLinkHelp)
        .accessibilityLabel(downloadTitle + ": " + release.title)
    }

    @ViewBuilder private func queuedView(_ item: Transfer) -> some View {
        if item.complete {
            HStack(spacing: 4) {
                KDStatusPill(tr("Скачано", "Downloaded"), tone: .success, systemImage: "checkmark")
                if !item.folder.isEmpty {
                    KDIconButton(systemImage: "folder", label: tr("Показать в папке", "Show in folder")) { reveal(item) }
                }
            }
        } else {
            Button {
                model.select(.downloads)
            } label: {
                KDStatusPill(tr("В загрузках", "In downloads") + " · " + KDFormat.percent(item.progress), tone: .accent, systemImage: "arrow.down.circle")
            }
            .buttonStyle(.plain)
            .help(tr("Открыть загрузки", "Open Downloads"))
            .accessibilityLabel(tr("В загрузках", "In downloads") + ", " + KDFormat.percent(item.progress))
        }
    }

    private func start() {
        if adding { return }
        adding = true
        Task { @MainActor in
            _ = await model.add(release, card: card)
            adding = false
        }
    }

    private func send(to device: LanDevice) {
        Task { @MainActor in
            await model.lan("send", ["device": .string(device.id), "release": .string(release.id), "card": .string(card.id)])
        }
    }

    private func reveal(_ item: Transfer) {
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: item.folder)])
    }
}

// MARK: Releases section

struct DetailReleasesSection: View {
    @EnvironmentObject private var model: AppModel
    @ObservedObject var transfers: TransferStore
    var card: MediaCard
    @State private var filters = DetailReleaseFilters()
    @State private var showsExtras = false
    @State private var limit = DetailReleasesSection.pageSize
    static let pageSize = 60

    private var isGame: Bool { card.section == "games" }
    private var isSeries: Bool { card.section == "series" }

    /// This title's queue entries by release title, so a row can show "В загрузках" or "Скачано".
    private var linkedTransfers: [String: Transfer] {
        var result: [String: Transfer] = [:]
        for item in transfers.items where item.mediaId == card.id {
            if item.complete && item.missing { continue }
            if let known = result[item.releaseTitle], known.complete { continue }
            result[item.releaseTitle] = item
        }
        return result
    }

    private var pairedDevices: [LanDevice] { (model.lanState?.devices ?? []).filter { $0.paired } }

    var body: some View {
        let all: [ReleaseRow] = model.releases(for: card)
        let facets = DetailReleaseFacets(rows: all, season: filters.season)
        let rows: [ReleaseRow] = filters.arrange(all)
        VStack(alignment: .leading, spacing: KD.Space.s) {
            header(all: all, shown: rows.count)
            if !all.isEmpty {
                ReleaseFilterBar(filters: $filters, showsExtras: $showsExtras, facets: facets, card: card, isGame: isGame, isSeries: isSeries)
                    .settingsLayoutProbe("detail.releases.toolbar")
            }
            SourceStatusView(sources: model.sources(for: card))
            listContent(all: all, rows: rows)
        }
        .onChange(of: filters) { limit = DetailReleasesSection.pageSize }
    }

    private func header(all: [ReleaseRow], shown: Int) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: KD.Space.xs) {
            Text(tr("Выбрать раздачу", "Choose a release"))
                .font(KDFont.section)
                .accessibilityAddTraits(.isHeader)
            if model.releasesLoading { ProgressView().controlSize(.small) }
            if filters.hidesRows && !all.isEmpty {
                Text(tr("Показано \(shown) из \(all.count)", "Showing \(shown) of \(all.count)"))
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: KD.Space.s)
            KDIconButton(systemImage: "arrow.clockwise", label: tr("Обновить список раздач", "Refresh releases")) {
                Task { @MainActor in await model.loadReleases(card) }
            }
            .disabled(model.releasesLoading)
        }
    }

    @ViewBuilder private func listContent(all: [ReleaseRow], rows: [ReleaseRow]) -> some View {
        if all.isEmpty {
            if model.releasesLoading { loadingSkeleton } else { noReleases }
        } else if rows.isEmpty {
            KDEmptyState(
                title: tr("Нет раздач с такими фильтрами", "No releases match these filters"),
                systemImage: "line.3.horizontal.decrease.circle",
                message: tr("Сбрось фильтры, чтобы увидеть все \(all.count).", "Reset the filters to see all \(all.count)."),
                actionTitle: tr("Сбросить фильтры", "Reset filters"),
                action: { filters = DetailReleaseFilters() }
            )
        } else {
            rowList(rows)
        }
    }

    private var loadingSkeleton: some View {
        VStack(spacing: 8) {
            ForEach(0..<4, id: \.self) { index in
                KDSkeletonBlock(radius: KD.Radius.control, lane: index).frame(height: 76)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(tr("Ищем раздачи…", "Searching for releases…"))
    }

    private var noReleases: some View {
        KDEmptyState(
            title: tr("Раздачи не найдены", "No releases found"),
            systemImage: "tray",
            message: tr("Источники пока ничего не нашли. Повтори поиск позже.", "The sources found nothing yet. Try again later."),
            actionTitle: tr("Повторить поиск", "Search again"),
            action: { Task { @MainActor in await model.loadReleases(card) } }
        )
    }

    private func rowList(_ rows: [ReleaseRow]) -> some View {
        let shown: [ReleaseRow] = Array(rows.prefix(limit))
        let remaining = rows.count - shown.count
        let linked = linkedTransfers
        let devices = pairedDevices
        return VStack(alignment: .leading, spacing: 8) {
            LazyVStack(spacing: 8) {
                ForEach(shown) { release in
                    ReleaseListRow(release: release, card: card, transfer: linked[release.title], devices: devices)
                }
            }
            if remaining > 0 {
                Button {
                    limit += DetailReleasesSection.pageSize
                } label: {
                    Text(tr("Показать ещё · осталось \(remaining)", "Show more · \(remaining) left"))
                }
                .buttonStyle(KDSecondaryButtonStyle())
            }
        }
    }
}
