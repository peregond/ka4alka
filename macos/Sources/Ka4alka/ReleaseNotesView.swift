import AppKit
import SwiftUI

/// What the newest published release says about itself.
struct ReleaseNotes: Equatable {
    var version: String
    var title: String
    var blocks: [ReleaseNotesBlock]
    var address: URL
}

enum ReleaseNotesBlock: Equatable {
    case subheading(String)
    case bullet(String)
    case paragraph(String)
}

enum ReleaseNotesError: Error { case invalid, unavailable }

/// Reads the notes of the latest GitHub release of this application and turns the Markdown text into a few block kinds.
/// Only this repository's own release is accepted, and the answer is size-limited.
enum ReleaseNotesSource {
    static let repository = "peregond/ka4alka"
    static let latest = URL(string: "https://api.github.com/repos/peregond/ka4alka/releases/latest")!
    static let page = URL(string: "https://github.com/peregond/ka4alka/releases/latest")!

    private struct Payload: Decodable {
        var tag_name: String?
        var name: String?
        var body: String?
        var draft: Bool?
        var prerelease: Bool?
        var html_url: String?
    }

    static func parse(_ data: Data) throws -> ReleaseNotes {
        guard data.count <= 256 * 1024 else { throw ReleaseNotesError.invalid }
        let payload: Payload = try JSONDecoder().decode(Payload.self, from: data)
        guard let tag = payload.tag_name, tag.range(of: #"^v\d+\.\d+\.\d+$"#, options: .regularExpression) != nil,
              payload.draft != true, payload.prerelease != true,
              let text = payload.html_url, let address = URL(string: text),
              address.scheme == "https", address.host == "github.com",
              address.path.hasPrefix("/" + repository + "/releases/") else { throw ReleaseNotesError.invalid }
        let body: String = (payload.body ?? "").replacingOccurrences(of: "\r\n", with: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !body.isEmpty, body.count <= 20_000 else { throw ReleaseNotesError.invalid }
        var blocks: [ReleaseNotesBlock] = []
        var title: String = payload.name ?? tag
        var paragraph: [String] = []
        var first = true
        func flush() {
            if !paragraph.isEmpty { blocks.append(ReleaseNotesBlock.paragraph(paragraph.joined(separator: " "))); paragraph = [] }
        }
        for raw in body.split(separator: "\n", omittingEmptySubsequences: false) {
            let line: String = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty { flush(); continue }
            if line.hasPrefix("# ") {
                flush()
                // The first heading is the headline of the release; the sheet shows it as its title.
                if first { title = String(line.dropFirst(2)) } else { blocks.append(ReleaseNotesBlock.subheading(String(line.dropFirst(2)))) }
            } else if line.hasPrefix("### ") || line.hasPrefix("## ") {
                flush()
                blocks.append(ReleaseNotesBlock.subheading(String(line.drop { $0 == "#" }.drop { $0 == " " })))
            } else if line.hasPrefix("- ") || line.hasPrefix("* ") {
                flush()
                blocks.append(ReleaseNotesBlock.bullet(String(line.dropFirst(2))))
            } else {
                paragraph.append(line)
            }
            first = false
        }
        flush()
        guard !blocks.isEmpty else { throw ReleaseNotesError.invalid }
        return ReleaseNotes(version: String(tag.dropFirst()), title: title, blocks: blocks, address: address)
    }

    static func load() async throws -> ReleaseNotes {
        var request = URLRequest(url: latest, timeoutInterval: 15)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("Kachalka-Mac", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw ReleaseNotesError.unavailable }
        return try parse(data)
    }

    /// Bold, italic, code and links inside one line of the notes.
    static func inline(_ text: String) -> AttributedString {
        var options = AttributedString.MarkdownParsingOptions()
        options.interpretedSyntax = .inlineOnlyPreservingWhitespace
        return (try? AttributedString(markdown: text, options: options)) ?? AttributedString(text)
    }
}

/// "Что нового": the notes of the newest release in a sheet, instead of a jump to the GitHub page.
struct ReleaseNotesSheet: View {
    @EnvironmentObject private var model: AppModel

    private enum Phase {
        case loading
        case ready(ReleaseNotes)
        case failed
    }
    @State private var phase: Phase = Phase.loading
    @State private var attempt = 0

    var body: some View {
        VStack(alignment: .leading, spacing: KD.Space.m) {
            header
            content
            footer
        }
        .padding(KD.Space.l)
        .frame(width: 560, height: 520)
        .task(id: attempt) { await load() }
    }

    // MARK: Pieces

    private var header: some View {
        VStack(alignment: .leading, spacing: KD.Space.xs) {
            HStack(spacing: KD.Space.xs) {
                Label(tr("Что нового", "What’s New"), systemImage: "sparkles")
                    .font(KDFont.kicker)
                    .foregroundStyle(KDPalette.accent)
                if case Phase.ready(let notes) = phase {
                    Text(notes.version)
                        .font(.caption.monospacedDigit().weight(.semibold))
                        .padding(.horizontal, KD.Space.xs)
                        .padding(.vertical, 2)
                        .background(KDPalette.accentSoft, in: Capsule())
                        .foregroundStyle(KDPalette.accent)
                        .accessibilityLabel(tr("Версия", "Version") + " " + notes.version)
                }
            }
            if case Phase.ready(let notes) = phase {
                Text(notes.title)
                    .font(KDFont.section)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.isHeader)
            } else {
                Text(tr("Новости Качалки", "News from Ka4alka"))
                    .font(KDFont.section)
                    .accessibilityAddTraits(.isHeader)
            }
        }
    }

    @ViewBuilder private var content: some View {
        switch phase {
        case .loading:
            VStack(spacing: KD.Space.s) {
                ProgressView().controlSize(.regular)
                Text(tr("Загружаем описание…", "Loading the release notes…"))
                    .font(KDFont.secondary)
                    .foregroundStyle(Color.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .failed:
            VStack(spacing: KD.Space.s) {
                Image(systemName: "wifi.exclamationmark")
                    .font(.system(size: 28))
                    .foregroundStyle(KDPalette.warning)
                    .accessibilityHidden(true)
                Text(tr("Не удалось загрузить описание. Проверь соединение или открой страницу выпуска.", "The release notes could not be loaded. Check the connection or open the release page."))
                    .font(KDFont.secondary)
                    .foregroundStyle(Color.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                Button(tr("Повторить", "Try Again")) { phase = Phase.loading; attempt += 1 }
                    .buttonStyle(KDSecondaryButtonStyle())
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .ready(let notes):
            ScrollView {
                VStack(alignment: .leading, spacing: KD.Space.s) {
                    ForEach(Array(notes.blocks.enumerated()), id: \.offset) { entry in block(entry.element) }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.trailing, KD.Space.xs)
            }
        }
    }

    @ViewBuilder private func block(_ value: ReleaseNotesBlock) -> some View {
        switch value {
        case .subheading(let text):
            Text(ReleaseNotesSource.inline(text))
                .font(KDFont.rowTitle)
                .padding(.top, KD.Space.xs)
                .accessibilityAddTraits(.isHeader)
        case .bullet(let text):
            HStack(alignment: .firstTextBaseline, spacing: KD.Space.s) {
                Circle().fill(KDPalette.accent).frame(width: 6, height: 6).accessibilityHidden(true)
                Text(ReleaseNotesSource.inline(text))
                    .font(KDFont.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        case .paragraph(let text):
            Text(ReleaseNotesSource.inline(text))
                .font(KDFont.secondary)
                .foregroundStyle(Color.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
        }
    }

    private var footer: some View {
        HStack {
            Button(tr("Открыть на GitHub", "Open on GitHub")) { NSWorkspace.shared.open(pageAddress) }
                .buttonStyle(KDSecondaryButtonStyle())
            Spacer()
            Button(tr("Закрыть", "Close")) { model.releaseNotes = false }
                .buttonStyle(KDPrimaryButtonStyle())
                .keyboardShortcut(.defaultAction)
        }
    }

    private var pageAddress: URL {
        if case Phase.ready(let notes) = phase { return notes.address }
        return ReleaseNotesSource.page
    }

    private func load() async {
        do {
            let notes: ReleaseNotes = try await ReleaseNotesSource.load()
            if !Task.isCancelled { phase = Phase.ready(notes) }
        } catch {
            if !Task.isCancelled { phase = Phase.failed }
        }
    }
}
