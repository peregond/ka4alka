import AppKit
import XCTest
@testable import Ka4alka

/// The "drawn position equals layout position" measurement, on synthetic screenshots.
@MainActor final class HitAuditTests: XCTestCase {
    private let background: NSColor = NSColor(srgbRed: 14.0 / 255.0, green: 17.0 / 255.0, blue: 21.0 / 255.0, alpha: 1)
    /// The accent at 14 % over the dark sidebar, as the selected row looks.
    private let tinted: NSColor = NSColor(srgbRed: 25.0 / 255.0, green: 46.0 / 255.0, blue: 43.0 / 255.0, alpha: 1)
    /// A neutral hover fill: brighter than the background but not green.
    private let hover: NSColor = NSColor(srgbRed: 40.0 / 255.0, green: 43.0 / 255.0, blue: 47.0 / 255.0, alpha: 1)

    private func makeBitmap(width: Int, height: Int) -> NSBitmapImageRep? {
        return NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .calibratedRGB, bytesPerRow: width * 4, bitsPerPixel: 32)
    }

    /// A background with one band of `bandColor` on the pixel rows `bandRows`, drawn the way a window screenshot is
    /// (through a graphics context), so the pixel format matches what `cacheDisplay` produces.
    private func makeShot(width: Int, height: Int, bandRows: Range<Int>, bandColor: NSColor) -> NSBitmapImageRep? {
        guard let bitmap = makeBitmap(width: width, height: height) else { return nil }
        guard let context = NSGraphicsContext(bitmapImageRep: bitmap) else { return nil }
        let saved: NSGraphicsContext? = NSGraphicsContext.current
        NSGraphicsContext.current = context
        background.setFill()
        NSRect(x: 0, y: 0, width: width, height: height).fill()
        if !bandRows.isEmpty {
            // The context is bottom-up; the helper counts rows from the top.
            let bottomUpY: Int = height - bandRows.upperBound
            bandColor.setFill()
            NSRect(x: 0, y: bottomUpY, width: width, height: bandRows.count).fill()
        }
        context.flushGraphics()
        NSGraphicsContext.current = saved
        return bitmap
    }

    func testSyntheticPixelsReadBackAsDrawn() throws {
        let bitmap: NSBitmapImageRep = try XCTUnwrap(makeShot(width: 40, height: 100, bandRows: 20..<40, bandColor: tinted))
        let inside: SmokeRGB = try XCTUnwrap(SmokeHitAudit.color(in: bitmap, x: 6, y: 30))
        let outside: SmokeRGB = try XCTUnwrap(SmokeHitAudit.color(in: bitmap, x: 6, y: 70))
        XCTAssertEqual(inside.green, 46.0 / 255.0, accuracy: 0.02)
        XCTAssertEqual(outside.green, 17.0 / 255.0, accuracy: 0.02)
    }

    func testFindsTheTintedBand() throws {
        guard let bitmap = makeShot(width: 40, height: 200, bandRows: 60..<92, bandColor: tinted) else { return XCTFail("no bitmap") }
        let baseline: Int? = SmokeHitAudit.quietRow(in: bitmap, columnPx: 6, fromPx: 0, toPx: 200)
        let quiet: Int = try XCTUnwrap(baseline)
        XCTAssertFalse((60..<92).contains(quiet), "the quiet row must be background, got \(quiet)")
        let run = SmokeHitAudit.highlightRun(in: bitmap, columnPx: 6, baselinePx: quiet, fromPx: 0, toPx: 200, minLengthPx: 16)
        let bandPixel: SmokeRGB? = SmokeHitAudit.color(in: bitmap, x: 6, y: 70)
        let basePixel: SmokeRGB? = SmokeHitAudit.color(in: bitmap, x: 6, y: quiet)
        let found = try XCTUnwrap(run, "band pixel \(String(describing: bandPixel)), background pixel \(String(describing: basePixel))")
        XCTAssertEqual(found.startPx, 60)
        XCTAssertEqual(found.endPx, 91)
        let centre: CGFloat = SmokeHitAudit.runCentrePoints(startPx: found.startPx, endPx: found.endPx, scale: 1)
        XCTAssertEqual(centre, 76, accuracy: 0.01)
    }

    func testAlignedBandIsAccepted() throws {
        guard let bitmap = makeShot(width: 40, height: 200, bandRows: 60..<92, bandColor: tinted) else { return XCTFail("no bitmap") }
        let frame: CGRect = CGRect(x: 0, y: 60, width: 40, height: 32)
        let measured = SmokeHitAudit.measureRow(in: bitmap, scale: 1, origin: CGPoint.zero, layoutFrame: frame)
        let result = try XCTUnwrap(measured)
        XCTAssertEqual(result.offset, 0, accuracy: 1)
        XCTAssertTrue(result.aligned)
    }

    func testBandShiftedBySeventyPixelsIsReportedAsMisaligned() throws {
        guard let bitmap = makeShot(width: 40, height: 220, bandRows: 130..<162, bandColor: tinted) else { return XCTFail("no bitmap") }
        let frame: CGRect = CGRect(x: 0, y: 60, width: 40, height: 32)
        let measured = SmokeHitAudit.measureRow(in: bitmap, scale: 1, origin: CGPoint.zero, layoutFrame: frame)
        let result = try XCTUnwrap(measured)
        XCTAssertEqual(result.drawnCentre, 146, accuracy: 0.01)
        XCTAssertEqual(result.layoutCentre, 76, accuracy: 0.01)
        XCTAssertEqual(result.offset, 70, accuracy: 1)
        XCTAssertFalse(result.aligned)
    }

    func testRetinaScaleAndContentOriginAreApplied() throws {
        // 2 pt per pixel pair: the band covers pixel rows 160..<224, i.e. points 80...111 of the screenshot.
        // The content view starts 4 pt below the top of the screenshot, so the layout frame sits at y = 76.
        guard let bitmap = makeShot(width: 80, height: 400, bandRows: 160..<224, bandColor: tinted) else { return XCTFail("no bitmap") }
        let frame: CGRect = CGRect(x: 0, y: 76, width: 40, height: 32)
        let measured = SmokeHitAudit.measureRow(in: bitmap, scale: 2, origin: CGPoint(x: 0, y: 4), layoutFrame: frame)
        let result = try XCTUnwrap(measured)
        XCTAssertEqual(result.drawnCentre, 92, accuracy: 0.01)
        XCTAssertEqual(result.layoutCentre, 92, accuracy: 0.01)
        XCTAssertTrue(result.aligned)
    }

    func testToleranceIsEightPoints() {
        XCTAssertTrue(SmokeHitAudit.isAligned(offset: 8))
        XCTAssertTrue(SmokeHitAudit.isAligned(offset: -8))
        XCTAssertFalse(SmokeHitAudit.isAligned(offset: 8.5))
        XCTAssertFalse(SmokeHitAudit.isAligned(offset: -34))
        XCTAssertEqual(SmokeHitAudit.offsetPoints(drawnCentre: 146, layoutCentre: 76), 70, accuracy: 0.001)
    }

    func testNeutralHoverFillIsNotTakenForTheHighlight() throws {
        guard let bitmap = makeShot(width: 40, height: 200, bandRows: 60..<92, bandColor: hover) else { return XCTFail("no bitmap") }
        let run = SmokeHitAudit.highlightRun(in: bitmap, columnPx: 6, baselinePx: 10, fromPx: 0, toPx: 200, minLengthPx: 16)
        XCTAssertNil(run)
        let frame: CGRect = CGRect(x: 0, y: 60, width: 40, height: 32)
        XCTAssertNil(SmokeHitAudit.measureRow(in: bitmap, scale: 1, origin: CGPoint.zero, layoutFrame: frame))
    }

    func testMissingBandAndBadInputNeverCrash() {
        guard let bitmap = makeShot(width: 40, height: 100, bandRows: 0..<0, bandColor: tinted) else { return XCTFail("no bitmap") }
        let frame: CGRect = CGRect(x: 0, y: 10, width: 40, height: 32)
        XCTAssertNil(SmokeHitAudit.measureRow(in: bitmap, scale: 1, origin: CGPoint.zero, layoutFrame: frame))
        XCTAssertNil(SmokeHitAudit.measureRow(in: bitmap, scale: 0, origin: CGPoint.zero, layoutFrame: frame))
        let outside: CGRect = CGRect(x: 500, y: 10, width: 40, height: 32)
        XCTAssertNil(SmokeHitAudit.measureRow(in: bitmap, scale: 1, origin: CGPoint.zero, layoutFrame: outside))
        XCTAssertNil(SmokeHitAudit.highlightRun(in: bitmap, columnPx: -3, baselinePx: 0))
        XCTAssertNil(SmokeHitAudit.quietRow(in: bitmap, columnPx: 6, fromPx: 50, toPx: 20))
        XCTAssertNil(SmokeHitAudit.color(in: bitmap, x: 99, y: 0))
    }

    func testTintClassifier() {
        let base: SmokeRGB = SmokeRGB(red: 0.055, green: 0.067, blue: 0.082)
        let accentOverDark: SmokeRGB = SmokeRGB(red: 0.098, green: 0.180, blue: 0.169)
        let grey: SmokeRGB = SmokeRGB(red: 0.157, green: 0.169, blue: 0.184)
        XCTAssertTrue(SmokeHitAudit.isTinted(accentOverDark, baseline: base))
        XCTAssertFalse(SmokeHitAudit.isTinted(grey, baseline: base))
        XCTAssertFalse(SmokeHitAudit.isTinted(base, baseline: base))
        // Light theme: the accent darkens the background, green less than red and blue.
        let paper: SmokeRGB = SmokeRGB(red: 0.925, green: 0.925, blue: 0.925)
        let accentOverLight: SmokeRGB = SmokeRGB(red: 0.80, green: 0.857, blue: 0.843)
        XCTAssertTrue(SmokeHitAudit.isTinted(accentOverLight, baseline: paper))
    }

    func testSmokeShotKeepsADefaultContentOrigin() throws {
        let bitmap: NSBitmapImageRep = try XCTUnwrap(makeBitmap(width: 4, height: 4))
        let shot: SmokeShot = SmokeShot(bitmap: bitmap, scale: 1, bounds: CGRect(x: 0, y: 0, width: 4, height: 4), isSheet: false)
        XCTAssertEqual(shot.contentOrigin, CGPoint.zero)
    }
}
