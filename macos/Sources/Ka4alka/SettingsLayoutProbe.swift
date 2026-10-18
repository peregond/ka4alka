import SwiftUI

// Capture real SwiftUI layout in the packaged native smoke test. Normal app
// launches do not install geometry readers or retain this diagnostic state.
@MainActor enum SettingsLayoutMeasurements {
    static var frames: [String: CGRect] = [:]
}

extension View {
    @ViewBuilder @MainActor func settingsLayoutProbe(_ id: String) -> some View {
        if CommandLine.arguments.contains("--smoke-test") {
            background {
                GeometryReader { geometry in
                    let frame = geometry.frame(in: .global)
                    Color.clear
                        .onAppear { SettingsLayoutMeasurements.frames[id] = frame }
                        .onChange(of: frame) { _, next in SettingsLayoutMeasurements.frames[id] = next }
                }
            }
        } else {
            self
        }
    }
}
