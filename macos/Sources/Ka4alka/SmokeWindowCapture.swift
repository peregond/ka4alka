import AppKit
import Foundation

/// Smoke-test fallback only. Captures this app's explicit window id, without shadow or other windows.
/// Failure to obtain real pixels remains a failed check; no synthetic image or relaxed pixel threshold.
@MainActor enum SmokeWindowCapture {
    static func bitmap(windowNumber: Int) async throws -> NSBitmapImageRep {
        let path = FileManager.default.temporaryDirectory.appendingPathComponent("ka4alka-window-" + UUID().uuidString + ".png")
        defer { try? FileManager.default.removeItem(at: path) }
        let filename = path.path
        let data: Data = try await Task.detached {
            let command = Process()
            let error = Pipe()
            command.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
            command.arguments = ["-x", "-o", "-l", String(windowNumber), "-t", "png", filename]
            command.standardOutput = FileHandle.nullDevice
            command.standardError = error
            try command.run()
            DispatchQueue.global().asyncAfter(deadline: .now() + 10) {
                if command.isRunning { command.terminate() }
            }
            command.waitUntilExit()
            guard command.terminationStatus == 0 else {
                let message = String(data: error.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
                throw UpdateError("Could not capture the composited application window: " + String(message.prefix(300)))
            }
            return try Data(contentsOf: URL(fileURLWithPath: filename))
        }.value
        guard let bitmap = NSBitmapImageRep(data: data) else { throw UpdateError("Invalid composited window screenshot") }
        return bitmap
    }
}
