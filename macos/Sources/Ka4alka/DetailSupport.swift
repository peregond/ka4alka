import AppKit
import SwiftUI

// MARK: Small helpers

/// Windows shows "В ролях / Режиссёр / Оператор" under the portraits; the engine sends the plural group names.
func detailRoleTitle(_ role: String) -> String {
    switch role {
    case "Актёры": return tr("В ролях", "Cast")
    case "Режиссёры": return tr("Режиссёр", "Director")
    case "Операторы": return tr("Оператор", "Cinematographer")
    default: return tr(role)
    }
}

/// Only https links leave the app.
func detailSecureURL(_ text: String?) -> URL? {
    guard let text, let url = URL(string: text), url.scheme == "https", url.host != nil else { return nil }
    return url
}

/// Three grey lines while a text is loading.
struct DetailSkeletonLines: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            KDSkeletonBlock(radius: 5, lane: 0).frame(maxWidth: KD.Width.reading).frame(height: 14)
            KDSkeletonBlock(radius: 5, lane: 1).frame(maxWidth: KD.Width.reading).frame(height: 14)
            KDSkeletonBlock(radius: 5, lane: 2).frame(maxWidth: 420).frame(height: 14)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(tr("Загрузка…", "Loading…"))
    }
}

// MARK: Expandable text

private struct DetailShownHeightKey: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = nextValue() }
}

private struct DetailFullHeightKey: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = nextValue() }
}

/// A reading column that clamps to a few lines and offers "Читать дальше" only when the text is really cut.
struct DetailExpandableText: View {
    var text: String
    var lines: Int = 4
    var expandTitle: String = tr("Читать дальше", "Read more")
    @State private var expanded = false
    @State private var shownHeight: CGFloat = 0
    @State private var fullHeight: CGFloat = 0

    private var canExpand: Bool { fullHeight > shownHeight + 2 }

    var body: some View {
        let limit: Int? = expanded ? nil : lines
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            Text(text)
                .font(.body)
                .lineSpacing(4)
                .lineLimit(limit)
                .textSelection(.enabled)
                .frame(maxWidth: KD.Width.reading, alignment: .leading)
                .background(GeometryReader { proxy in
                    Color.clear.preference(key: DetailShownHeightKey.self, value: proxy.size.height)
                })
                .background(alignment: .topLeading) { measurer }
            if canExpand {
                Button(expanded ? tr("Свернуть", "Collapse") : expandTitle) { expanded.toggle() }
                    .buttonStyle(.plain)
                    .font(.callout.weight(.semibold))
                    .foregroundStyle(KDPalette.accent)
                    .accessibilityHint(expanded ? "" : tr("Показать весь текст", "Show the whole text"))
            }
        }
        .onPreferenceChange(DetailShownHeightKey.self) { value in
            if !expanded { shownHeight = value }
        }
        .onPreferenceChange(DetailFullHeightKey.self) { value in
            fullHeight = value
        }
    }

    /// The same text without a line limit, invisible, only to learn its full height.
    private var measurer: some View {
        Text(text)
            .font(.body)
            .lineSpacing(4)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: KD.Width.reading, alignment: .leading)
            .background(GeometryReader { proxy in
                Color.clear.preference(key: DetailFullHeightKey.self, value: proxy.size.height)
            })
            .hidden()
            .accessibilityHidden(true)
    }
}

// MARK: Cast

struct DetailPersonCell: View {
    @EnvironmentObject private var model: AppModel
    var person: Person

    var body: some View {
        Button {
            model.open(person)
        } label: {
            VStack(alignment: .leading, spacing: 6) {
                Artwork(address: person.photoUrl, width: 240, placeholderSymbol: "person.crop.rectangle")
                    .aspectRatio(4.0 / 5.0, contentMode: .fit)
                    .clipShape(RoundedRectangle(cornerRadius: KD.Radius.thumb, style: .continuous))
                Text(person.name)
                    .font(Font.callout.weight(.medium))
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                Text(detailRoleTitle(person.role))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(KDPosterButtonStyle(radius: KD.Radius.thumb))
        .accessibilityLabel(person.name + ", " + detailRoleTitle(person.role))
    }
}

struct DetailCastSection: View {
    var people: [Person]
    @State private var expanded = false
    @State private var width: CGFloat = 0
    private let spacing: CGFloat = 12
    private let minimum: CGFloat = 110

    private var perRow: Int {
        if width <= 0 { return 6 }
        return max(1, Int((width + spacing) / (minimum + spacing)))
    }
    private var columns: [GridItem] { [GridItem(.adaptive(minimum: minimum), spacing: spacing)] }

    var body: some View {
        let hiddenCount = max(0, people.count - perRow)
        let shown: [Person] = expanded ? people : Array(people.prefix(perRow))
        VStack(alignment: .leading, spacing: KD.Space.s) {
            HStack(alignment: .firstTextBaseline) {
                Text(tr("Актёры и создатели", "Cast and crew"))
                    .font(KDFont.section)
                    .accessibilityAddTraits(.isHeader)
                Spacer(minLength: KD.Space.s)
                if hiddenCount > 0 {
                    Button(expanded ? tr("Свернуть", "Collapse") : tr("Ещё \(hiddenCount)", "\(hiddenCount) more")) { expanded.toggle() }
                        .buttonStyle(.plain)
                        .font(.callout.weight(.semibold))
                        .foregroundStyle(KDPalette.accent)
                }
            }
            LazyVGrid(columns: columns, alignment: .leading, spacing: KD.Space.m) {
                ForEach(Array(shown.enumerated()), id: \.offset) { _, person in
                    DetailPersonCell(person: person)
                }
            }
            .kdReadWidth($width)
        }
    }
}

/// Shown when a film or series has no people yet.
struct DetailCastEmpty: View {
    var onRetry: () -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            Text(tr("Актёры и создатели", "Cast and crew"))
                .font(KDFont.section)
                .accessibilityAddTraits(.isHeader)
            Text(tr("Участники пока недоступны у источника.", "The source has no cast list yet."))
                .font(.callout)
                .foregroundStyle(.secondary)
            Button(tr("Повторить", "Retry"), action: onRetry)
                .buttonStyle(KDSecondaryButtonStyle())
                .controlSize(.small)
        }
    }
}

// MARK: Awards

struct DetailAwardsSection: View {
    var awards: [CinemaAward]
    @State private var expanded = false

    var body: some View {
        DisclosureGroup(tr("Награды", "Awards"), isExpanded: $expanded) {
            VStack(alignment: .leading, spacing: KD.Space.xs) {
                ForEach(Array(awards.enumerated()), id: \.offset) { _, award in
                    awardRow(award)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.top, KD.Space.xs)
        }
    }

    private func awardRow(_ award: CinemaAward) -> some View {
        let state = award.winner ? tr("Победитель", "Winner") : tr("Номинация", "Nominee")
        let tone: KDTone = award.winner ? KDTone.warning : KDTone.neutral
        let symbol: String = award.winner ? "trophy.fill" : "laurel.leading"
        return HStack(alignment: .firstTextBaseline, spacing: KD.Space.xs) {
            KDStatusPill(state, tone: tone, systemImage: symbol)
            Text("\(award.name) · \(award.year) · \(award.category)").font(.callout)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: Game facts

struct GameFactsCard: View {
    var game: GameDetail
    var item: MediaCard

    private var reviewTone: KDTone {
        switch item.store?.reviewTone ?? "" {
        case "positive": return KDTone.success
        case "negative": return KDTone.danger
        case "mixed": return KDTone.warning
        default:
            guard let review = game.review else { return KDTone.neutral }
            if review.percent >= 70 { return KDTone.success }
            if review.percent >= 40 { return KDTone.warning }
            return KDTone.danger
        }
    }

    private func compactCount(_ value: Int) -> String {
        value.formatted(.number.notation(.compactName).locale(KDFormat.locale))
    }

    private func reviewText(_ review: GameReviews) -> String {
        var parts: [String] = []
        if let label = item.store?.reviewLabel, !label.isEmpty { parts.append(label) }
        parts.append(String(review.percent) + "%")
        if review.count > 0 { parts.append(tr("из ", "of ") + compactCount(review.count)) }
        return parts.joined(separator: " · ")
    }

    private var isFree: Bool { item.store?.free ?? false }
    private var steamURL: URL? { detailSecureURL(game.steamUrl) }
    private var steamDbURL: URL? { detailSecureURL(game.steamDbUrl) }
    private var traits: String { game.traits.filter { !$0.isEmpty }.joined(separator: ", ") }

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            Grid(alignment: .leading, horizontalSpacing: KD.Space.l, verticalSpacing: KD.Space.s) {
                textRow(tr("Разработчик", "Developer"), game.developers)
                textRow(tr("Издатель", "Publisher"), game.publishers)
                textRow(tr("Дата выхода", "Release date"), game.releaseDate)
                textRow(tr("Режимы", "Modes"), traits)
                russianRow
                reviewRow
                metacriticRow
                ageRow
                priceRow
            }
            if steamURL != nil || steamDbURL != nil {
                KDFlowLayout(spacing: KD.Space.m, lineSpacing: KD.Space.xs) {
                    if let url = steamURL { Link("Steam", destination: url).foregroundStyle(KDPalette.accent) }
                    if let url = steamDbURL { Link("SteamDB", destination: url).foregroundStyle(KDPalette.accent) }
                }
                .font(.callout.weight(.semibold))
            }
        }
        .padding(KD.Space.m)
        .frame(maxWidth: .infinity, alignment: .leading)
        .kdCard()
    }

    private func factLabel(_ title: String) -> some View {
        Text(title)
            .font(KDFont.meta)
            .foregroundStyle(.secondary)
            .gridColumnAlignment(.leading)
    }

    @ViewBuilder private func textRow(_ title: String, _ value: String) -> some View {
        if !value.isEmpty {
            GridRow {
                factLabel(title)
                Text(value).font(KDFont.secondary).textSelection(.enabled)
            }
        }
    }

    @ViewBuilder private var russianRow: some View {
        if game.russian != "Unknown" {
            GridRow {
                factLabel(tr("Русский язык", "Russian language"))
                russianPill
            }
        }
    }

    @ViewBuilder private var russianPill: some View {
        if game.russian == "Absent" {
            KDStatusPill(tr("Нет русского", "No Russian"), tone: .neutral)
        } else if game.russian == "Voiced" {
            KDStatusPill(tr("Русская озвучка", "Russian audio"), tone: .success, systemImage: "speaker.wave.2")
        } else {
            KDStatusPill(tr("Русский интерфейс", "Russian interface"), tone: .info, systemImage: "text.bubble")
        }
    }

    @ViewBuilder private var reviewRow: some View {
        if let review = game.review {
            GridRow {
                factLabel(tr("Отзывы Steam", "Steam reviews"))
                KDStatusPill(reviewText(review), tone: reviewTone)
            }
        }
    }

    @ViewBuilder private var metacriticRow: some View {
        if game.metacritic > 0 {
            GridRow {
                factLabel("Metacritic")
                Text(String(game.metacritic)).font(Font.callout.weight(.semibold).monospacedDigit())
            }
        }
    }

    @ViewBuilder private var ageRow: some View {
        if game.age > 0 {
            GridRow {
                factLabel(tr("Возраст", "Age"))
                Text(String(game.age) + "+").font(KDFont.secondary)
            }
        }
    }

    @ViewBuilder private var priceRow: some View {
        if isFree || !game.price.isEmpty {
            GridRow {
                factLabel(tr("Цена", "Price"))
                priceCell
            }
        }
    }

    private var discountBadge: some View {
        Text("−" + String(game.discount) + "%")
            .font(Font.callout.weight(.bold).monospacedDigit())
            .foregroundStyle(KDPalette.accentInk)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(KDPalette.accentFill, in: RoundedRectangle(cornerRadius: 5, style: .continuous))
    }

    private var priceCell: some View {
        let original = game.originalPrice
        let help: String = original.isEmpty ? "" : tr("Без скидки: ", "Original price: ") + original
        return HStack(spacing: KD.Space.xs) {
            if game.discount > 0 { discountBadge }
            Text(isFree ? tr("Бесплатно", "Free") : game.price)
                .font(Font.callout.weight(.semibold))
                .help(help)
            if game.discount > 0 && !original.isEmpty {
                Text(original).font(.callout).strikethrough().foregroundStyle(.secondary)
            }
        }
    }
}

// MARK: System requirements

struct DetailRequirementLine: Identifiable {
    var id: Int
    var title: String
    var value: String
    var isHeading: Bool
}

/// Steam requirements arrive as one block: "Минимальные:" headings and "ОС: Windows 10" pairs.
func detailRequirementLines(_ text: String) -> [DetailRequirementLine] {
    var result: [DetailRequirementLine] = []
    let rawLines = text.split(whereSeparator: { $0.isNewline })
    for raw in rawLines {
        let line = String(raw).trimmingCharacters(in: .whitespaces)
        if line.isEmpty { continue }
        let identifier = result.count
        if let colon = line.firstIndex(of: ":") {
            let key = String(line[line.startIndex..<colon]).trimmingCharacters(in: .whitespaces)
            let value = String(line[line.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
            if value.isEmpty {
                result.append(DetailRequirementLine(id: identifier, title: key, value: "", isHeading: true))
            } else {
                result.append(DetailRequirementLine(id: identifier, title: key, value: value, isHeading: false))
            }
        } else if line.count <= 32 {
            result.append(DetailRequirementLine(id: identifier, title: line, value: "", isHeading: true))
        } else {
            result.append(DetailRequirementLine(id: identifier, title: "", value: line, isHeading: false))
        }
    }
    return result
}

struct DetailRequirementsView: View {
    var text: String
    @State private var expanded = false

    var body: some View {
        DisclosureGroup(tr("Системные требования", "System requirements"), isExpanded: $expanded) {
            Grid(alignment: .leading, horizontalSpacing: KD.Space.l, verticalSpacing: 6) {
                ForEach(detailRequirementLines(text)) { line in
                    requirementRow(line)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.top, KD.Space.xs)
        }
    }

    @ViewBuilder private func requirementRow(_ line: DetailRequirementLine) -> some View {
        if line.isHeading {
            GridRow {
                Text(line.title)
                    .font(KDFont.rowTitle)
                    .padding(.top, KD.Space.xs)
                    .gridCellColumns(2)
                    .accessibilityAddTraits(.isHeader)
            }
        } else {
            GridRow {
                Text(line.title).font(KDFont.meta).foregroundStyle(.secondary).gridColumnAlignment(.leading)
                Text(line.value).font(KDFont.secondary).textSelection(.enabled)
            }
        }
    }
}

// MARK: Gallery

struct GameGalleryView: View {
    var shots: [GameShot]
    @State private var selected = 0
    @FocusState private var focused: Bool

    private var index: Int { min(max(0, selected), max(0, shots.count - 1)) }

    var body: some View {
        if !shots.isEmpty {
            VStack(alignment: .leading, spacing: KD.Space.s) {
                stage
                thumbnails
                if let url = detailSecureURL(shots[index].full) {
                    Link(tr("Открыть в браузере", "Open in browser"), destination: url)
                        .font(.callout)
                        .foregroundStyle(KDPalette.accent)
                }
            }
        }
    }

    private var stage: some View {
        let position = tr("Скриншот \(index + 1) из \(shots.count)", "Screenshot \(index + 1) of \(shots.count)")
        return Artwork(address: shots[index].full, width: 1280)
            .aspectRatio(16.0 / 9.0, contentMode: .fit)
            .frame(maxWidth: 860)
            .clipShape(RoundedRectangle(cornerRadius: KD.Radius.card, style: .continuous))
            .overlay(alignment: .leading) { if index > 0 { arrow(-1) } }
            .overlay(alignment: .trailing) { if index < shots.count - 1 { arrow(1) } }
            .focusable()
            .focused($focused)
            .onKeyPress(.leftArrow) {
                step(-1)
                return .handled
            }
            .onKeyPress(.rightArrow) {
                step(1)
                return .handled
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(position)
    }

    private func arrow(_ direction: Int) -> some View {
        Button {
            step(direction)
        } label: {
            Image(systemName: direction < 0 ? "chevron.backward" : "chevron.forward")
        }
        .buttonStyle(KDOverlayButtonStyle(circular: true))
        .padding(KD.Space.xs)
        .help(direction < 0 ? tr("Предыдущий скриншот", "Previous screenshot") : tr("Следующий скриншот", "Next screenshot"))
        .accessibilityLabel(direction < 0 ? tr("Предыдущий скриншот", "Previous screenshot") : tr("Следующий скриншот", "Next screenshot"))
    }

    private func step(_ delta: Int) {
        let next = index + delta
        if next >= 0 && next < shots.count { selected = next }
    }

    private var thumbnails: some View {
        ScrollView(.horizontal) {
            LazyHStack(spacing: KD.Space.xs) {
                ForEach(Array(shots.enumerated()), id: \.element.id) { offset, shot in
                    thumbnail(shot, offset: offset)
                }
            }
            .padding(5)
        }
        .scrollIndicators(.hidden)
    }

    private func thumbnail(_ shot: GameShot, offset: Int) -> some View {
        let shape = RoundedRectangle(cornerRadius: 8, style: .continuous)
        let ring: Color = offset == index ? KDPalette.accent : Color.clear
        return Button {
            selected = offset
        } label: {
            Artwork(address: shot.thumb, width: 320)
                .frame(width: 140, height: 79)
                .clipShape(shape)
                .overlay(shape.strokeBorder(ring, lineWidth: 2))
        }
        .buttonStyle(KDPosterButtonStyle(radius: 8))
        .accessibilityLabel(tr("Скриншот \(offset + 1) из \(shots.count)", "Screenshot \(offset + 1) of \(shots.count)"))
        .accessibilityAddTraits(offset == index ? .isSelected : [])
    }
}
