import AppKit
import SwiftUI

struct PhoneRemoteSettings: View {
    @EnvironmentObject private var model: AppModel
    @State private var busy = false

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            Text(tr("С телефона через браузер", "From your phone browser")).font(KDFont.rowTitle)
            Text(tr("Подключись к той же домашней сети Wi-Fi. Открой адрес на телефоне и введи одноразовый код. Файлы скачает этот компьютер в выбранную папку.", "Use the same home Wi-Fi. Open the address on your phone and enter the one-time code. Files download to the folder selected on this computer."))
                .font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ViewThatFits(in: .horizontal) {
                HStack(spacing: KD.Space.s) { actions }
                VStack(alignment: .leading, spacing: KD.Space.s) { actions }
            }
            if let state = model.phoneState?.state, model.phoneState?.running == true {
                if state.urls.isEmpty {
                    Text(tr("Подключи компьютер к домашней сети Wi-Fi.", "Connect your computer to home Wi-Fi."))
                }
                ForEach(state.urls, id: \.self) { address in
                    Text(address).font(.system(.body, design: .monospaced)).textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
                if let code = state.code {
                    Text(tr("Код на 2 минуты: ", "Code, valid for 2 minutes: ") + code)
                        .font(.system(.body, design: .monospaced)).textSelection(.enabled)
                }
                Text(tr("Доступ работает до закрытия приложения. Используй доверенную сеть.", "Access ends when the app closes. Use a trusted network."))
                    .font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .task {
            await model.phone("state")
            while !Task.isCancelled {
                do { try await Task.sleep(for: .seconds(5)) } catch { break }
                if model.phoneState?.running == true { await model.phone("state") }
            }
        }
    }

    @ViewBuilder private var actions: some View {
        Button(tr("Подключить телефон", "Connect phone")) { perform("start") }
            .buttonStyle(KDSecondaryButtonStyle()).disabled(busy)
        if model.phoneState?.running == true {
            Button(tr("Отключить все телефоны", "Disconnect all phones")) { perform("stop") }
                .buttonStyle(KDSecondaryButtonStyle()).disabled(busy)
        }
    }
    private func perform(_ action: String) {
        busy = true
        Task { await model.phone(action); busy = false }
    }
}
