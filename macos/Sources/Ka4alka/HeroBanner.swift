import AppKit
import SwiftUI

/// A single visible hero uses the same bounded artwork cache as poster cards.
/// No timer preloads the remaining slides or redraws the catalog while scrolling.
struct CatalogSpotlightView: View {
    static let reservedHeight: CGFloat = 348
    var items: [MediaCard]
    @State private var selectedID: String?
    private var selected: MediaCard? { items.first { $0.id == selectedID } ?? items.first }

    var body: some View {
        if let selected {
            VStack(spacing: 12) {
                FeaturedHero(card: selected, eyebrow: tr("В центре внимания", "In the spotlight"))
                    .id(selected.id)
                if items.count > 1 {
                    HStack(spacing: 8) {
                        ForEach(items) { item in
                            Button { selectedID = item.id } label: {
                                Capsule().fill(item.id == selected.id ? Color.accentColor : Color.secondary.opacity(0.25))
                                    .frame(width: item.id == selected.id ? 24 : 7, height: 7)
                                    .padding(.vertical, 7)
                            }
                            .buttonStyle(.plain)
                            .accessibilityLabel(item.title)
                            .accessibilityValue(item.id == selected.id ? tr("Выбрано", "Selected") : "")
                        }
                        Spacer()
                        Button { advance(-1) } label: { Image(systemName: "chevron.left") }
                            .help(tr("Предыдущий баннер", "Previous banner"))
                            .accessibilityLabel(tr("Предыдущий баннер", "Previous banner"))
                        Button { advance(1) } label: { Image(systemName: "chevron.right") }
                            .help(tr("Следующий баннер", "Next banner"))
                            .accessibilityLabel(tr("Следующий баннер", "Next banner"))
                    }.controlSize(.small)
                }
            }
            .frame(height: Self.reservedHeight, alignment: .top)
            .accessibilityIdentifier("catalog-spotlight")
        }
    }

    private func advance(_ delta: Int) {
        guard !items.isEmpty else { return }
        let index = items.firstIndex { $0.id == selected?.id } ?? 0
        selectedID = items[(index + delta + items.count) % items.count].id
    }
}

struct HeroPlaceholder: View {
    var body: some View {
        RoundedRectangle(cornerRadius: 22, style: .continuous)
            .fill(LinearGradient(colors: [Color.accentColor.opacity(0.16), Color.primary.opacity(0.04)], startPoint: .topLeading, endPoint: .bottomTrailing))
            .overlay(alignment: .bottomLeading) {
                VStack(alignment: .leading, spacing: 12) {
                    RoundedRectangle(cornerRadius: 5).fill(.quaternary).frame(width: 110, height: 12)
                    RoundedRectangle(cornerRadius: 7).fill(.quaternary).frame(maxWidth: 280).frame(height: 30)
                    RoundedRectangle(cornerRadius: 5).fill(.quaternary).frame(width: 150, height: 13)
                }.padding(28)
            }.accessibilityHidden(true)
    }
}

struct FeaturedHero: View {
    @EnvironmentObject private var model: AppModel
    var card: MediaCard
    var eyebrow: String? = nil
    @State private var backdrop: String?
    @State private var localized: MediaCard?
    private var item: MediaCard { model.settings?.uiLanguage == "en" ? localized ?? card : card }
    private var lite: Bool { model.settings?.liteMode == true }
    private var requestID: String { card.id + ":" + (model.settings?.uiLanguage ?? "ru") }

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .bottomLeading) {
                Button { model.open(item) } label: {
                    ZStack(alignment: .bottomLeading) {
                        HeroBackground(poster: item.poster, backdrop: backdrop, lite: lite)
                            .frame(width: geometry.size.width, height: geometry.size.height)
                        LinearGradient(colors: [.black.opacity(0.05), .black.opacity(0.78)], startPoint: .top, endPoint: .bottom)
                        LinearGradient(colors: [.black.opacity(0.55), .clear], startPoint: .leading, endPoint: .trailing)
                        heroText(compact: geometry.size.width < 580)
                            .frame(maxWidth: min(geometry.size.width - 48, 640), alignment: .leading)
                            .padding(24).padding(.bottom, 54)
                    }
                    .contentShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
                }.buttonStyle(.plain)
                    .accessibilityLabel(item.title + ", " + tr("Подробнее", "Details"))
                    .accessibilityIdentifier("hero-open")
                HStack(spacing: 10) {
                    Button { model.open(item) } label: { Label(tr("Подробнее", "Details"), systemImage: "info.circle") }
                        .buttonStyle(.borderedProminent)
                    Button { Task { await model.save(card) } } label: {
                        Image(systemName: model.isSaved(card) ? "heart.fill" : "heart")
                            .foregroundStyle(model.isSaved(card) ? .pink : .white)
                    }
                    .buttonStyle(.bordered)
                    .tint(.white)
                    .help(model.isSaved(card) ? tr("Удалить из сохранённого", "Remove from Saved") : tr("Сохранить", "Save"))
                    .accessibilityLabel(model.isSaved(card) ? tr("Удалить из сохранённого", "Remove from Saved") : tr("Сохранить", "Save"))
                }.controlSize(.large).padding(24)
            }
            .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
            .overlay { RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(.white.opacity(0.12), lineWidth: 1).allowsHitTesting(false) }
        }
        .frame(height: 300)
        .task(id: requestID) {
            backdrop = nil
            let result = try? await model.bridge.call("artwork", ["id": .string(card.id)])
            if !Task.isCancelled {
                let address = result?["url"].string ?? ""
                backdrop = address.isEmpty ? nil : address
            }
        }
        .task(id: "localized-" + requestID) {
            localized = nil
            if model.settings?.uiLanguage == "en", card.section != "games" {
                let result = try? await model.bridge.call("localizedCard", ["id": .string(card.id)], as: MediaCard.self)
                if !Task.isCancelled { localized = result }
            }
        }
    }

    private func heroText(compact: Bool) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            if let eyebrow { Label(eyebrow, systemImage: "sparkles").font(.caption.weight(.semibold)).foregroundStyle(.white.opacity(0.8)) }
            Text(item.title).font(.system(size: compact ? 28 : 36, weight: .bold)).lineLimit(2)
            HStack(spacing: 10) {
                if !item.rating.isEmpty, item.rating != "—" { Label(item.rating, systemImage: "star.fill").foregroundStyle(.yellow) }
                Text([item.year > 0 ? String(item.year) : "", item.genre].filter { !$0.isEmpty }.joined(separator: " · ")).foregroundStyle(.white.opacity(0.8))
            }.font(.callout).lineLimit(1)
            if !item.description.isEmpty {
                Text(item.description).font(.callout).foregroundStyle(.white.opacity(0.9)).lineLimit(compact ? 2 : 3)
            }
        }.foregroundStyle(.white).frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct HeroBackground: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    var poster: String?
    var backdrop: String?
    var lite: Bool
    @State private var image: NSImage?

    var body: some View {
        ZStack {
            LinearGradient(colors: [Color(red: 0.04, green: 0.23, blue: 0.27), Color(red: 0.08, green: 0.09, blue: 0.18)], startPoint: .topTrailing, endPoint: .bottomLeading)
            if !lite, !reduceTransparency {
                Artwork(address: poster, width: 180).blur(radius: 26, opaque: true).opacity(0.55)
            }
            if let image { Image(nsImage: image).resizable().scaledToFill().transition(.opacity) }
        }.clipped()
        .task(id: (backdrop ?? "") + (lite ? ":lite" : ":full")) {
            image = nil
            guard let backdrop else { return }
            let result = await ImageStore.shared.image(backdrop, width: lite ? 640 : 1280)
            if !Task.isCancelled { withAnimation(reduceMotion || lite ? nil : .easeInOut(duration: 0.25)) { image = result } }
        }
        .onDisappear { image = nil }
    }
}
