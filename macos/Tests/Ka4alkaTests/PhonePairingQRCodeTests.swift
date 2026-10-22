import CoreImage
import XCTest
@testable import Ka4alka

@MainActor final class PhonePairingQRCodeTests: XCTestCase {
    private func decoded(_ image: CIImage) -> [String] {
        let reader = CIDetector(ofType: CIDetectorTypeQRCode, context: CIContext(), options: [CIDetectorAccuracy: CIDetectorAccuracyHigh])
        return reader?.features(in: image).compactMap { ($0 as? CIQRCodeFeature)?.messageString } ?? []
    }
    func testNativeQRConnectsToTheCorrectAddressWithoutQueryCredentials() throws {
        for address in ["http://192.168.1.40:45834/", "http://192.168.222.222:45834/"] {
            // Fixed, non-live fixture code; no server credentials are stored in this test.
            let link = PhonePairingQRCode.link(address: address, code: "123456")
            let url = try XCTUnwrap(URL(string: link))
            XCTAssertNil(url.query)
            let image = try XCTUnwrap(PhonePairingQRCode.image(link: link))
            let cgImage = try XCTUnwrap(image.cgImage(forProposedRect: nil, context: nil, hints: nil))
            XCTAssertEqual(decoded(CIImage(cgImage: cgImage)), [link])
        }
    }
    func testWindowsGeneratedQRIsReadableByTheAppleDecoder() throws {
        let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
        let data = try Data(contentsOf: root.appendingPathComponent("Fixtures/windows-phone-pairing.png"))
        let image = try XCTUnwrap(CIImage(data: data))
        XCTAssertEqual(decoded(image), ["http://192.168.1.40:45834/#pair=123456"])
    }
}
