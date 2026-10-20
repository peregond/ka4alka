import AppKit
import Foundation

// Pixel measuring for the "drawn position equals layout position" smoke check.
// The layout probes record the frame that RECEIVES a click. A tinted row can still be DRAWN somewhere
// else (a ScrollView plus safeAreaInset in a sidebar column once drew the rows two rows lower than
// their hit areas), and layout frames alone never notice that. These pure functions find the tinted
// band in a screenshot so the smoke test can compare it with the probe frame.
// Nothing here runs during a normal launch.

/// A colour with components from 0 to 1 in sRGB.
struct SmokeRGB: Equatable {
    var red: Double
    var green: Double
    var blue: Double
}

/// Where a highlight was drawn compared with where its probe says the click area is (points, top-left origin).
struct SmokeHitMeasurement {
    var startPx: Int
    var endPx: Int
    var drawnCentre: CGFloat
    var layoutCentre: CGFloat
    var offset: CGFloat
    var aligned: Bool
}

enum SmokeHitAudit {
    /// Smallest change of any colour channel (0...1) that counts as "different from the background".
    static let minDelta: Double = 0.03
    /// How much more the green channel must move than the red one. The highlight is a 14 % mix of the
    /// green accent, so a neutral hover fill or a shadow does not pass this test.
    static let minTint: Double = 0.02
    /// The largest accepted distance between the drawn and the layout centre, in points.
    static let tolerancePoints: CGFloat = 8

    /// The colour of one pixel in sRGB. `y` counts from the top of the image. Nil outside the bitmap.
    static func color(in bitmap: NSBitmapImageRep, x: Int, y: Int) -> SmokeRGB? {
        guard x >= 0, y >= 0, x < bitmap.pixelsWide, y < bitmap.pixelsHigh else { return nil }
        guard let raw = bitmap.colorAt(x: x, y: y) else { return nil }
        guard let converted = raw.usingColorSpace(NSColorSpace.sRGB) else { return nil }
        let red: Double = Double(converted.redComponent)
        let green: Double = Double(converted.greenComponent)
        let blue: Double = Double(converted.blueComponent)
        return SmokeRGB(red: red, green: green, blue: blue)
    }

    /// Whether `pixel` is the accent tint over `baseline`: clearly different and greener than the background.
    static func isTinted(_ pixel: SmokeRGB, baseline: SmokeRGB) -> Bool {
        let deltaRed: Double = pixel.red - baseline.red
        let deltaGreen: Double = pixel.green - baseline.green
        let deltaBlue: Double = pixel.blue - baseline.blue
        let largest: Double = max(abs(deltaRed), max(abs(deltaGreen), abs(deltaBlue)))
        // The accent is green: against the background the green channel moves clearly more than the red one
        // (up on a dark background, less down on a light one). A neutral fill moves all channels alike.
        let tint: Double = deltaGreen - deltaRed
        return largest >= minDelta && tint >= minTint
    }

    private static func median(_ values: [Double]) -> Double {
        if values.isEmpty { return 0 }
        let sorted: [Double] = values.sorted()
        return sorted[sorted.count / 2]
    }

    /// A pixel row of the column that shows the plain background: the row whose colour is closest to the
    /// per-channel median of the whole column. The highlight covers a small part of the column, so the
    /// median is the sidebar background. Nil when the range is empty or unreadable.
    static func quietRow(in bitmap: NSBitmapImageRep, columnPx: Int, fromPx: Int, toPx: Int) -> Int? {
        let first: Int = max(0, fromPx)
        let last: Int = min(bitmap.pixelsHigh, toPx)
        guard last > first, columnPx >= 0, columnPx < bitmap.pixelsWide else { return nil }
        var rows: [Int] = []
        var reds: [Double] = []
        var greens: [Double] = []
        var blues: [Double] = []
        var y: Int = first
        while y < last {
            if let pixel = color(in: bitmap, x: columnPx, y: y) {
                rows.append(y)
                reds.append(pixel.red)
                greens.append(pixel.green)
                blues.append(pixel.blue)
            }
            y += 1
        }
        guard !rows.isEmpty else { return nil }
        let middleRed: Double = median(reds)
        let middleGreen: Double = median(greens)
        let middleBlue: Double = median(blues)
        var bestRow: Int = rows[0]
        var bestDistance: Double = Double.greatestFiniteMagnitude
        for index in 0..<rows.count {
            let dr: Double = abs(reds[index] - middleRed)
            let dg: Double = abs(greens[index] - middleGreen)
            let db: Double = abs(blues[index] - middleBlue)
            let distance: Double = max(dr, max(dg, db))
            if distance < bestDistance {
                bestDistance = distance
                bestRow = rows[index]
            }
        }
        return bestRow
    }

    /// The longest contiguous run of tinted pixels in a pixel column (rows counted from the top, `endPx`
    /// inclusive), measured against the colour at `baselinePx`. Runs shorter than `minLengthPx` are ignored.
    static func highlightRun(in bitmap: NSBitmapImageRep, columnPx: Int, baselinePx: Int, fromPx: Int = 0, toPx: Int = Int.max, minLengthPx: Int = 4) -> (startPx: Int, endPx: Int)? {
        guard let baseline = color(in: bitmap, x: columnPx, y: baselinePx) else { return nil }
        let first: Int = max(0, fromPx)
        let last: Int = min(bitmap.pixelsHigh, toPx)
        guard last > first else { return nil }
        var best: (startPx: Int, endPx: Int)?
        var bestLength: Int = 0
        var runStart: Int = -1
        var y: Int = first
        while y <= last {
            var tinted: Bool = false
            if y < last, let pixel = color(in: bitmap, x: columnPx, y: y) {
                tinted = isTinted(pixel, baseline: baseline)
            }
            if tinted {
                if runStart < 0 { runStart = y }
            } else if runStart >= 0 {
                let length: Int = y - runStart
                if length >= minLengthPx && length > bestLength {
                    best = (startPx: runStart, endPx: y - 1)
                    bestLength = length
                }
                runStart = -1
            }
            y += 1
        }
        return best
    }

    /// Vertical centre of a pixel run in points (the run covers the pixel rows start...end).
    static func runCentrePoints(startPx: Int, endPx: Int, scale: CGFloat) -> CGFloat {
        let safeScale: CGFloat = scale > 0 ? scale : 1
        let middle: CGFloat = CGFloat(startPx + endPx + 1) / 2
        return middle / safeScale
    }

    /// Signed distance from the layout centre to the drawn centre, in points.
    static func offsetPoints(drawnCentre: CGFloat, layoutCentre: CGFloat) -> CGFloat {
        return drawnCentre - layoutCentre
    }

    static func isAligned(offset: CGFloat, tolerance: CGFloat = tolerancePoints) -> Bool {
        return abs(offset) <= tolerance
    }

    /// Finds the tinted row inside a screenshot and compares it with its probe frame.
    ///  - `scale`: pixels per point of the screenshot;
    ///  - `origin`: where the SwiftUI `.global` space starts inside the screenshot, in points from its top left;
    ///  - `layoutFrame`: the probe frame in `.global` coordinates.
    /// The scanned pixel column lies `insetPoints` right of the frame's left edge, which is inside the rounded
    /// highlight but left of the icon. Nil when no highlight is found (a measuring problem, not a defect).
    static func measureRow(in bitmap: NSBitmapImageRep, scale: CGFloat, origin: CGPoint, layoutFrame: CGRect, insetPoints: CGFloat = 6, tolerance: CGFloat = tolerancePoints) -> SmokeHitMeasurement? {
        guard scale > 0, layoutFrame.width > insetPoints * 2, layoutFrame.height > 0 else { return nil }
        let columnPoints: CGFloat = layoutFrame.minX + insetPoints + origin.x
        let columnPx: Int = Int((columnPoints * scale).rounded())
        guard columnPx >= 0, columnPx < bitmap.pixelsWide else { return nil }
        guard let baselinePx = quietRow(in: bitmap, columnPx: columnPx, fromPx: 0, toPx: bitmap.pixelsHigh) else { return nil }
        let minLength: Int = max(2, Int((layoutFrame.height * scale * 0.5).rounded()))
        guard let run = highlightRun(in: bitmap, columnPx: columnPx, baselinePx: baselinePx, fromPx: 0, toPx: bitmap.pixelsHigh, minLengthPx: minLength) else { return nil }
        let drawnInShot: CGFloat = runCentrePoints(startPx: run.startPx, endPx: run.endPx, scale: scale)
        let drawnCentre: CGFloat = drawnInShot - origin.y
        let layoutCentre: CGFloat = layoutFrame.midY
        let offset: CGFloat = offsetPoints(drawnCentre: drawnCentre, layoutCentre: layoutCentre)
        let aligned: Bool = isAligned(offset: offset, tolerance: tolerance)
        return SmokeHitMeasurement(startPx: run.startPx, endPx: run.endPx, drawnCentre: drawnCentre, layoutCentre: layoutCentre, offset: offset, aligned: aligned)
    }
}
