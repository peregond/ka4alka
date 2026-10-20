import XCTest
@testable import Ka4alka

@MainActor final class SearchNavigationTests: XCTestCase {
    func testBackRestoresBrowseFiltersPageAndScrollAfterSearch() {
        let model = AppModel()
        model.bridge.disabled = true
        model.select(.series)
        model.filter.genre = "comedy"
        model.page = 3
        model.browseAnchor = "series:park"
        model.openSearch()
        XCTAssertEqual(model.route, .search)
        XCTAssertEqual(model.section, .movies)
        XCTAssertFalse(model.filter.active)
        model.query = "South Park"
        model.submitSearch()
        XCTAssertEqual(model.route, .search)
        XCTAssertEqual(model.submittedQuery, "South Park")
        model.back()
        XCTAssertEqual(model.route, .section(.series))
        XCTAssertEqual(model.section, .series)
        XCTAssertEqual(model.filter.genre, "comedy")
        XCTAssertEqual(model.page, 3)
        XCTAssertEqual(model.browseAnchor, "series:park")
        XCTAssertEqual(model.submittedQuery, "")
    }

    func testOpeningResultAndReturningKeepsSearchAndDoesNotDuplicateItsRoute() throws {
        let model = AppModel()
        model.bridge.disabled = true
        model.select(.games)
        model.openSearch()
        model.query = "Portal"
        model.submitSearch()
        model.open(try TestFixtures.card("games:portal", "Portal", section: "games"))
        model.back()
        XCTAssertEqual(model.route, .search)
        XCTAssertEqual(model.section, .games)
        XCTAssertEqual(model.submittedQuery, "Portal")
        let count = model.routes.count
        let focus = model.searchFocus
        model.openSearch()
        XCTAssertEqual(model.routes.count, count)
        XCTAssertGreaterThan(model.searchFocus, focus)
    }

    func testDraftDoesNotSubmitAndClearDoesNotOpenUnfilteredCatalog() {
        let model = AppModel()
        model.bridge.disabled = true
        model.openSearch()
        model.query = "Dune"
        XCTAssertEqual(model.submittedQuery, "")
        model.submitSearch()
        model.clearSearch()
        XCTAssertEqual(model.route, .search)
        XCTAssertEqual(model.query, "")
        XCTAssertEqual(model.submittedQuery, "")
        XCTAssertNil(model.list)
    }

    func testHistoryIsBoundedDeduplicatedAndScopedAndBlankIsNotRecorded() {
        let model = AppModel()
        model.bridge.disabled = true
        model.openSearch()
        for value in 0..<12 { model.query = "Title \(value)"; model.submitSearch() }
        XCTAssertEqual(model.recentSearches.count, 8)
        model.query = "title 11"; model.submitSearch()
        XCTAssertEqual(model.recentSearches.count, 8)
        XCTAssertEqual(model.recentSearches.first?.query, "title 11")
        model.setSearchGames(true)
        model.submitSearch()
        XCTAssertEqual(model.recentSearches.first?.games, true)
        XCTAssertEqual(model.recentSearches.filter { $0.query == "title 11" }.count, 2)
        model.query = "  "
        model.submitSearch()
        XCTAssertFalse(model.recentSearches.contains { $0.query.trimmingCharacters(in: .whitespaces).isEmpty })
    }

    func testSidebarSelectionLeavesSearchWithoutLeakingQuery() {
        let model = AppModel()
        model.bridge.disabled = true
        model.openSearch()
        model.query = "Dune"; model.submitSearch()
        model.select(.movies)
        XCTAssertEqual(model.route, .section(.movies))
        XCTAssertEqual(model.submittedQuery, "")
        XCTAssertEqual(model.query, "")
    }

    func testScrollChromeIgnoresBounceAndChangesOnlyAcrossThresholds() {
        XCTAssertFalse(ShellChromeVisibility.visible(offset: -30, wasVisible: false))
        XCTAssertFalse(ShellChromeVisibility.visible(offset: 55, wasVisible: false))
        XCTAssertTrue(ShellChromeVisibility.visible(offset: 56, wasVisible: false))
        XCTAssertTrue(ShellChromeVisibility.visible(offset: 20, wasVisible: true))
        XCTAssertFalse(ShellChromeVisibility.visible(offset: 8, wasVisible: true))
        XCTAssertFalse(ShellChromeVisibility.visible(offset: 0, wasVisible: true))
    }
}
