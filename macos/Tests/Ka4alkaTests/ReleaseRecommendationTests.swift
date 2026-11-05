import XCTest
@testable import Ka4alka

@MainActor final class ReleaseRecommendationTests: XCTestCase {
    private func row(_ id: String, studio: String, rank: Int?, recommended: Bool = false, quality: String = "Full HD") throws -> ReleaseRow {
        var item = try TestFixtures.decode(ReleaseRow.self, TestFixtures.releaseJSON(id: id, title: "Backrooms 2026"))
        item.studios = [studio]; item.recommendationOrder = rank; item.recommended = recommended
        item.quality = quality; item.russianAudio = true; item.russianDub = true
        return item
    }
    func testStudioAndQualityFiltersSelectVisibleDub() throws {
        let global = try row("global", studio: "HDrezka", rank: 0, recommended: true)
        let flarrow4k = try row("flarrow4k", studio: "Flarrow Films", rank: 1, quality: "4K")
        let flarrowHD = try row("flarrowHD", studio: "Flarrow Films", rank: 2)
        var filters = DetailReleaseFilters(); filters.studio = "Flarrow Films"
        let selected = filters.arrange([global, flarrowHD, flarrow4k])
        XCTAssertEqual(selected.first?.id, "flarrow4k")
        XCTAssertEqual(selected.filter(\.recommended).map(\.id), ["flarrow4k"])
        filters.quality = "Full HD"
        XCTAssertEqual(filters.arrange([global, flarrow4k, flarrowHD]).filter(\.recommended).map(\.id), ["flarrowHD"])
        XCTAssertFalse(flarrowHD.recommended, "Filters must not mutate the engine's source rows")
    }
    func testNoEligibleReleaseDoesNotInventRecommendation() throws {
        let unavailable = try row("unknown", studio: "Flarrow Films", rank: nil)
        XCTAssertFalse(DetailReleaseFilters().arrange([unavailable]).contains(where: \.recommended))
        let legacy = try row("old-engine", studio: "HDrezka", rank: nil, recommended: true)
        XCTAssertTrue(DetailReleaseFilters().arrange([legacy]).first?.recommended == true)
    }
    func testRussianAudioToggleDoesNotConfuseVoiceoverAndDubbing() throws {
        var voice = try row("voice", studio: "HDrezka", rank: 3)
        voice.russianDub = false; voice.audioTypes = ["Многоголосая"]
        var filters = DetailReleaseFilters(); filters.russianOnly = true
        XCTAssertTrue(filters.matches(voice))
        filters.audio = "Дубляж"
        XCTAssertFalse(filters.matches(voice))
    }
    func testHeroUsesSameFilteredSelectionAsReleaseTable() throws {
        let model = AppModel(); model.bridge.disabled = true
        let card = try TestFixtures.card("movies:backrooms", "Backrooms")
        let global = try row("global", studio: "HDrezka", rank: 0, recommended: true)
        let selected = try row("flarrow", studio: "Flarrow Films", rank: 1)
        model.releasesOwner = card.id; model.releases = [global, selected]
        XCTAssertEqual(model.recommendedRelease(for: card)?.id, "global")
        model.scopedReleaseRecommendation = ScopedReleaseRecommendation(owner: card.id, id: selected.id)
        XCTAssertEqual(model.recommendedRelease(for: card)?.id, "flarrow")
        model.scopedReleaseRecommendation = ScopedReleaseRecommendation(owner: card.id, id: nil)
        XCTAssertNil(model.recommendedRelease(for: card))
    }
}
