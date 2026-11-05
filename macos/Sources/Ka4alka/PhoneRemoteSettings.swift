import AppKit
import SwiftUI

struct PhoneRemoteSettings: View {
    @EnvironmentObject private var model: AppModel
    @State private var busy = false
    @State private var selectedAddress = ""
    @State private var qrImage: NSImage?

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.s) {
            Text(tr("С телефона через браузер", "From your phone browser")).font(KDFont.rowTitle)
            Text(tr("Подключись к той же домашней сети Wi-Fi и отсканируй QR-код камерой телефона. Подключение произойдёт автоматически. Файлы скачает этот компьютер в выбранную папку.", "Use the same home Wi-Fi and scan the QR code with your phone camera to connect automatically. Files download to the folder selected on this computer."))
                .font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ViewThatFits(in: .horizontal) {
                HStack(spacing: KD.Space.s) { actions }
                VStack(alignment: .leading, spacing: KD.Space.s) { actions }
            }
            if let state = model.phoneState?.state, model.phoneState?.running == true {
                if state.urls.isEmpty {
                    Text(tr("Подключи компьютер к домашней сети Wi-Fi.", "Connect your computer to home Wi-Fi."))
                }
                if let qrImage, state.code != nil {
                    Image(nsImage: qrImage).interpolation(.none).resizable().scaledToFit()
                        .frame(width: 200, height: 200).padding(8).background(.white)
                        .accessibilityLabel(tr("QR-код подключения телефона", "Phone pairing QR code"))
                    Text(tr("QR-код действует 2 минуты и только для одного подключения.", "The QR code expires in 2 minutes and can be used once."))
                        .font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                if state.urls.count > 1 {
                    Picker(tr("Адрес для подключения", "Connection address"), selection: $selectedAddress) {
                        ForEach(state.urls, id: \.self) { Text($0).tag($0) }
                    }.pickerStyle(.menu)
                }
                ForEach(state.urls, id: \.self) { address in
                    Text(address).font(.system(.body, design: .monospaced)).textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
                if !selectedAddress.isEmpty {
                    Button(tr("Копировать ссылку", "Copy link")) {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(selectedAddress, forType: .string)
                    }.buttonStyle(KDSecondaryButtonStyle())
                }
                if let code = state.code {
                    Text(tr("Код на 2 минуты: ", "Code, valid for 2 minutes: ") + code)
                        .font(.system(.body, design: .monospaced)).textSelection(.enabled)
                }
                Text(tr("Доступ работает до закрытия приложения. Используй доверенную сеть.", "Access ends when the app closes. Use a trusted network."))
                    .font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .onChange(of: model.phoneState?.state?.urls ?? [], initial: true) { _, addresses in
            if !addresses.contains(selectedAddress) { selectedAddress = addresses.first ?? "" }
        }
        .onChange(of: qrLink, initial: true) { _, link in
            if let link { qrImage = PhonePairingQRCode.image(link: link) } else { qrImage = nil }
        }
        .task {
            await model.phone("state")
            while !Task.isCancelled {
                do { try await Task.sleep(for: .seconds(5)) } catch { break }
                if model.phoneState?.running == true { await model.phone("state") }
            }
        }
    }

    private var qrLink: String? {
        guard model.phoneState?.running == true, !selectedAddress.isEmpty,
              let code = model.phoneState?.state?.code else { return nil }
        return PhonePairingQRCode.link(address: selectedAddress, code: code)
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
