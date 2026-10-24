import CoreGraphics
import Foundation
import Darwin

let arguments = CommandLine.arguments
if arguments.count == 2 && arguments[1] == "display" {
    let display = CGMainDisplayID()
    let modes = CGDisplayCopyAllDisplayModes(display, nil) as? [CGDisplayMode] ?? []
    if let mode = modes.filter({ $0.width >= 1600 && $0.height >= 1100 }).min(by: { $0.width * $0.height < $1.width * $1.height }) {
        let result = CGDisplaySetDisplayMode(display, mode, nil)
        print("Display configuration:", result.rawValue)
    }
    print("Capture desktop:", CGDisplayPixelsWide(display), "x", CGDisplayPixelsHigh(display))
    exit(0)
}
guard arguments.count == 2, let pid = Int(arguments[1]) else { exit(2) }
let rows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
guard let window = rows.first(where: {
    guard $0[kCGWindowOwnerPID as String] as? Int == pid,
          $0[kCGWindowLayer as String] as? Int == 0,
          let bounds = $0[kCGWindowBounds as String] as? [String: Any],
          let width = bounds["Width"] as? Double else { return false }
    return width > 700
}), let id = window[kCGWindowNumber as String] as? Int else { exit(1) }
print(id)
