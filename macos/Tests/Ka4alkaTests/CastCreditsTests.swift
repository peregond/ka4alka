import XCTest
@testable import Ka4alka

@MainActor final class CastCreditsTests: XCTestCase {
    func testOptionalCharacterPreservesOldActorsAndPersonNavigation() throws {
        let old = try JSONDecoder().decode(Person.self, from: Data(#"{"name":"Actor","role":"Актёры","pageUrl":""}"#.utf8))
        var credit = old
        credit.character = "Mark Scout"
        XCTAssertNil(old.character)
        XCTAssertEqual(old.id, credit.id)
        XCTAssertEqual(detailRoleTitle(credit.role, character: credit.character), "Mark Scout")
        XCTAssertEqual(detailRoleTitle(old.role, character: "  "), detailRoleTitle(old.role))
        XCTAssertEqual(detailRoleTitle("Режиссёры", character: "Wrong"), detailRoleTitle("Режиссёры"))
    }
    func testGameCardsAndPlaceholdersKeepFilmPosterProportions() {
        XCTAssertEqual(KD.posterAspect(wide: true), KD.posterAspect(wide: false))
        XCTAssertEqual(KD.posterAspect(wide: true), 2.0 / 3.0, accuracy: 0.001)
        XCTAssertEqual(KD.Size.wideCardWidth, KD.Size.posterWidth)
    }
}
