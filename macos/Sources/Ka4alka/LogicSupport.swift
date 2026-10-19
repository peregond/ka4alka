import AppKit
import SwiftUI
import UserNotifications

/// Loading state of the Featured page. It lives in the model so it survives the page being rebuilt on Back.
enum LoadState: Equatable { case idle, loading, failed(String), empty }

/// Reply of the engine's `gameShelves` call.
struct LogicGameShelfList: Decodable { var items: [GameShelfDefinition] }

/// How the app is drawn. `stored` is read from UserDefaults synchronously, so the very first frame already uses the right theme.
enum AppearanceMode: String, CaseIterable, Identifiable {
    case system, light, dark
    static let defaultsKey = "Ka4alkaAppearance"
    var id: String { rawValue }
    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light: return ColorScheme.light
        case .dark: return ColorScheme.dark
        }
    }
    var nsAppearance: NSAppearance? {
        switch self {
        case .system: return nil
        case .light: return NSAppearance(named: .aqua)
        case .dark: return NSAppearance(named: .darkAqua)
        }
    }
    /// True once the person (or the one-time migration from the old `light` setting) has stored a choice.
    static var hasStored: Bool {
        guard let raw = UserDefaults.standard.string(forKey: defaultsKey) else { return false }
        return AppearanceMode(rawValue: raw) != nil
    }
    /// The stored choice; before the first migration the app keeps the historical default, dark.
    static var stored: AppearanceMode {
        guard let raw = UserDefaults.standard.string(forKey: defaultsKey), let mode = AppearanceMode(rawValue: raw) else { return .dark }
        return mode
    }
}

/// Banner while the app is frontmost and a tap that opens Downloads. Callbacks arrive on the main queue.
final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate, @unchecked Sendable {
    var onOpenDownloads: (@Sendable () -> Void)?
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .sound])
    }
    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse, withCompletionHandler completionHandler: @escaping () -> Void) {
        let open = onOpenDownloads
        DispatchQueue.main.async {
            open?()
            completionHandler()
        }
    }
}

/// Diagnostics for failures that must not interrupt the person (ErrorLog-style: one line on stderr).
func logicLog(_ message: String) {
    FileHandle.standardError.write(Data(("[Ka4alka] " + message + "\n").utf8))
}
