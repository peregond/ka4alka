import SwiftUI
import XCTest
@testable import Ka4alka

final class FranchiseTests: XCTestCase {
    func testOneRowTracksPosterGridWidthsRatherThanFixedFilmCount() {
        XCTAssertEqual(CollapsibleFilmGrid.columns(for: 145), 1)
        XCTAssertEqual(CollapsibleFilmGrid.columns(for: 306), 2)
        XCTAssertEqual(CollapsibleFilmGrid.columns(for: 628), 4)
        XCTAssertEqual(CollapsibleFilmGrid.columns(for: 1111), 7)
        XCTAssertEqual(CollapsibleFilmGrid.columns(for: 0), 1)
    }
    func testCollectionReplyRemainsDistinctFromTheMainCatalog() throws {
        let data = Data(#"{"name":"Harry Potter","items":[]}"#.utf8)
        let result = try JSONDecoder().decode(FranchiseResult.self, from: data)
        XCTAssertEqual(result.name, "Harry Potter")
        XCTAssertTrue(result.items.isEmpty)
    }
}
