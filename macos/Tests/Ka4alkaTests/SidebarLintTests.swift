import XCTest

/// The sidebar column is a plain stack of buttons with the footer at the bottom. A ScrollView, a List or a
/// safe-area inset in that column drew its rows lower than the area that receives the click on macOS 26 and
/// later, so a click on one section opened another. The smoke test cannot see it (a window screenshot does not
/// contain the sidebar on those systems), so the structure is pinned here.
final class SidebarLintTests: XCTestCase {
    func testSidebarColumnIsAPlainStackOfButtons() throws {
        let text: String = try TestSources.text(of: "RootView.swift")
        guard let start = text.range(of: "private var sidebar: some View {") else {
            return XCTFail("RootView.swift no longer declares `private var sidebar: some View`")
        }
        guard let end = text.range(of: "private var showsUpdateStatus", range: start.upperBound..<text.endIndex) else {
            return XCTFail("could not find the end of the sidebar declaration")
        }
        let body: String = String(text[start.lowerBound..<end.lowerBound])
        let forbidden: [String] = ["ScrollView", "List(", "List {", ".listStyle", "safeAreaInset"]
        for word in forbidden {
            XCTAssertFalse(body.contains(word), "the sidebar must not use \(word): it draws rows away from their click areas on macOS 26 and later")
        }
        XCTAssertTrue(body.contains("sidebarFooter"), "the footer belongs in the sidebar stack")
    }

    func testPinnedRowsAreButtons() throws {
        let text: String = try TestSources.text(of: "RootView.swift")
        guard let start = text.range(of: "struct ShellPinnedRow: View {") else {
            return XCTFail("RootView.swift no longer declares ShellPinnedRow")
        }
        let tail: String = String(text[start.lowerBound...])
        XCTAssertTrue(tail.contains("Button(action: action)"), "sidebar entries are plain buttons")
    }
}
