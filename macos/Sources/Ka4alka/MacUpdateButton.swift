import SwiftUI

/// One update action for the sidebar and Settings. Only received-byte events move
/// the fill; no timer, fake percentage or separately animating progress row.
struct MacUpdateButton: View {
    @ObservedObject var updater: MacUpdater
    var expanded = false
    var probeID = "settings.update.action"
    @Environment(\.controlSize) private var controlSize

    private var fraction: Double? {
        guard let value = updater.progressValue, value.isFinite else { return nil }
        return min(1, max(0, value))
    }
    private var help: String { updater.busy && !updater.statusText.isEmpty ? updater.statusText : updater.buttonTitle }

    var body: some View {
        Group {
            if updater.busy {
                Button {} label: {
                    HStack(spacing: 6) {
                        if fraction == nil {
                            ProgressView().controlSize(.mini).tint(.white)
                        } else {
                            Image(systemName: "arrow.down").accessibilityHidden(true)
                        }
                        Text(updater.buttonTitle).lineLimit(1).minimumScaleFactor(0.8)
                        if let fraction {
                            Text(KDFormat.percent(fraction * 100)).monospacedDigit().lineLimit(1)
                        }
                    }
                    .font(.body.weight(.semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, controlSize == .large ? 18 : 14)
                    .padding(.vertical, controlSize == .large ? 8 : 6)
                    .frame(maxWidth: expanded ? .infinity : nil, minHeight: 24)
                    .background {
                        GeometryReader { geometry in
                            ZStack(alignment: .leading) {
                                Color(red: 0.055, green: 0.31, blue: 0.23)
                                Color(red: 0.11, green: 0.48, blue: 0.35)
                                    .frame(width: geometry.size.width * (fraction ?? 0))
                                    .settingsLayoutProbe(probeID + ".fill", group: probeID)
                            }
                        }
                    }
                    .clipShape(Capsule())
                    .overlay(Capsule().strokeBorder(.white.opacity(0.2), lineWidth: 1).allowsHitTesting(false))
                }
                .buttonStyle(.plain)
                .disabled(true)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(updater.buttonTitle)
                .accessibilityValue(fraction.map { KDFormat.percent($0 * 100) } ?? updater.statusText)
            } else {
                Button { Task { await updater.primaryAction() } } label: {
                    Label(updater.buttonTitle, systemImage: "arrow.triangle.2.circlepath")
                        .lineLimit(1)
                        .frame(maxWidth: expanded ? .infinity : nil)
                }
                .buttonStyle(KDPrimaryButtonStyle())
                .disabled(updater.installing)
            }
        }
        .help(help)
        .settingsLayoutProbe(probeID, kind: .control)
    }
}
