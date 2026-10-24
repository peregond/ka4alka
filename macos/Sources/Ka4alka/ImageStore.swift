import AppKit
import ImageIO
import SwiftUI

private let artworkMaximumBytes = 12 * 1024 * 1024

actor ArtworkDiagnostics {
    static let shared = ArtworkDiagnostics()
    private var errors: [String] = []
    func record(_ host: String, _ message: String) { if errors.count < 20 { errors.append(host + ": " + message) } }
    func snapshot() -> [String] { errors }
}
actor ImageSlots {
    private let limit: Int
    private var active = 0
    private var waiters: [(UUID, CheckedContinuation<Void, Error>)] = []
    init(limit: Int = 3) { self.limit = limit }
    func acquire() async throws {
        let id = UUID()
        try await withTaskCancellationHandler(operation: {
            try Task.checkCancellation()
            if active < limit { active += 1; return }
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
                if Task.isCancelled { continuation.resume(throwing: CancellationError()) }
                else { waiters.append((id, continuation)) }
            }
        }, onCancel: { Task { await self.cancel(id) } })
    }
    private func cancel(_ id: UUID) {
        guard let index = waiters.firstIndex(where: { $0.0 == id }) else { return }
        waiters.remove(at: index).1.resume(throwing: CancellationError())
    }
    func release() { if waiters.isEmpty { active -= 1 } else { waiters.removeFirst().1.resume() } }
}
/// Decoded artwork shared by the store and by `Artwork`'s synchronous first-frame lookup. NSCache is thread-safe.
final class ArtworkMemory: @unchecked Sendable {
    static let shared = ArtworkMemory()
    private let cache = NSCache<NSString, NSImage>()
    init() { cache.totalCostLimit = 96 * 1024 * 1024 }
    func image(forKey key: String) -> NSImage? { cache.object(forKey: key as NSString) }
    func store(_ image: NSImage, forKey key: String, cost: Int) { cache.setObject(image, forKey: key as NSString, cost: cost) }
    func setLimit(_ bytes: Int) { cache.totalCostLimit = bytes }
    func removeAll() { cache.removeAllObjects() }
}
@MainActor final class ImageStore {
    static let shared = ImageStore()
    weak var engine: EngineBridge?
    private struct Request { var id: UUID; var task: Task<CGImage?, Never>; var subscribers: Set<UUID> }
    private(set) var loadedCount = 0
    private(set) var failedCount = 0
    private var inflight: [String: Request] = [:]
    var pendingCount: Int { inflight.count }
    private var failures: [String: Date] = [:]
    private var failureOrder: [String] = []
    private let failureLimit = 120
    private let failureLifetime: TimeInterval = 20
    private let slots = ImageSlots(limit: 3)
    private let fallbackSlots = ImageSlots(limit: 1)
    private let session: URLSession
    init() {
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 8; config.timeoutIntervalForResource = 20; config.httpMaximumConnectionsPerHost = 3
        config.urlCache = URLCache(memoryCapacity: 4 * 1024 * 1024, diskCapacity: 128 * 1024 * 1024, diskPath: "Ka4alkaArtwork")
        session = URLSession(configuration: config)
    }
    /// Decoded pictures are kept in memory up to 24 MB in Lite mode and 96 MB otherwise.
    func setLite(_ lite: Bool) {
        let megabytes: Int = lite ? 24 : 96
        ArtworkMemory.shared.setLimit(megabytes * 1024 * 1024)
    }
    /// Forgets which pictures failed, so a retry (catalog refresh, restarted engine) tries them again at once.
    func clearFailures() {
        failures.removeAll(); failureOrder.removeAll()
    }
    func clear() {
        ArtworkMemory.shared.removeAll(); clearFailures()
        session.configuration.urlCache?.removeAllCachedResponses()
    }
    nonisolated static func cacheKey(_ address: String, width: Int) -> String { address + "|" + String(width) }
    /// Synchronous lookup so a poster that was already decoded appears in the first frame instead of flashing grey.
    nonisolated static func cachedImage(_ address: String?, width: Int) -> NSImage? {
        guard let address, !address.isEmpty else { return nil }
        return ArtworkMemory.shared.image(forKey: cacheKey(address, width: width))
    }
    func cached(_ address: String?, width: Int) -> NSImage? { ImageStore.cachedImage(address, width: width) }
    private func unsubscribe(_ key: String, _ subscriber: UUID) {
        guard var request = inflight[key] else { return }
        request.subscribers.remove(subscriber)
        if request.subscribers.isEmpty { request.task.cancel(); inflight.removeValue(forKey: key) }
        else { inflight[key] = request }
    }
    private func recentlyFailed(_ key: String) -> Bool {
        guard let date = failures[key] else { return false }
        if Date().timeIntervalSince(date) < failureLifetime { return true }
        failures.removeValue(forKey: key); failureOrder.removeAll { $0 == key }
        return false
    }
    private func recordFailure(_ key: String) {
        if failures[key] == nil {
            if failureOrder.count >= failureLimit, let oldest = failureOrder.first { failureOrder.removeFirst(); failures.removeValue(forKey: oldest) }
            failureOrder.append(key)
        }
        failures[key] = Date()
    }
    /// `//host/path` is https; `http` and every other scheme can only go through the engine's trusted relay.
    private static func target(_ address: String) -> (url: URL, direct: Bool)? {
        let text = address.hasPrefix("//") ? "https:" + address : address
        guard let url = URL(string: text), let scheme = url.scheme?.lowercased(), url.host != nil else { return nil }
        if scheme == "https" { return (url, true) }
        if scheme == "http" { return (url, false) }
        return nil
    }
    nonisolated private static func download(_ url: URL, session: URLSession) async throws -> Data {
        var request = URLRequest(url: url); request.setValue("Mozilla/5.0 Ka4alka/macOS", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200, data.count <= artworkMaximumBytes else { throw URLError(.badServerResponse) }
        return data
    }
    nonisolated private static func fallback(_ url: URL, engine: EngineBridge) async throws -> Data {
        let answer = try await engine.call("coverFallback", ["url": .string(url.absoluteString)])
        let path = URL(fileURLWithPath: answer["path"].string)
        guard let size = try path.resourceValues(forKeys: [.fileSizeKey]).fileSize, size <= 2 * 1024 * 1024 else { throw URLError(.dataLengthExceedsMaximum) }
        return try Data(contentsOf: path)
    }
    /// `width` goes to the decoder unchanged as the largest side in pixels; nothing caps it.
    nonisolated private static func thumbnail(_ data: Data, width: Int) -> CGImage? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        let options: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: width, kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceShouldCacheImmediately: true]
        return CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary)
    }
    nonisolated private static func isCancellation(_ error: Error) -> Bool {
        if error is CancellationError { return true }
        return (error as? URLError)?.code == .cancelled
    }
    /// `width` is the largest side of the decoded picture in pixels (a portrait poster is narrower than that).
    func image(_ address: String, width: Int = 400) async -> NSImage? {
        let key = ImageStore.cacheKey(address, width: width); let subscriber = UUID()
        if let ready = ArtworkMemory.shared.image(forKey: key) { return ready }
        guard !Task.isCancelled, let destination = ImageStore.target(address), !recentlyFailed(key) else { return nil }
        let url = destination.url; let direct = destination.direct
        let task: Task<CGImage?, Never>; let requestID: UUID
        if var existing = inflight[key] {
            existing.subscribers.insert(subscriber); inflight[key] = existing; task = existing.task; requestID = existing.id
        } else {
            requestID = UUID()
            task = Task.detached(priority: .utility) { [slots, fallbackSlots, session, engine] () -> CGImage? in
                do {
                    try Task.checkCancellation()
                    var data: Data?
                    var directError: Error?
                    if direct {
                        try await slots.acquire()
                        do { data = try await ImageStore.download(url, session: session) } catch { directError = error }
                        await slots.release()
                    }
                    try Task.checkCancellation()
                    if data == nil {
                        // The direct slot is already free: a stalled relay can never starve the posters that load directly.
                        guard let engine else { throw directError ?? URLError(.cannotLoadFromNetwork) }
                        try await fallbackSlots.acquire()
                        do { data = try await ImageStore.fallback(url, engine: engine) } catch { await fallbackSlots.release(); throw error }
                        await fallbackSlots.release()
                    }
                    try Task.checkCancellation()
                    guard let bytes = data else { throw URLError(.zeroByteResource) }
                    return ImageStore.thumbnail(bytes, width: width)
                } catch {
                    if !ImageStore.isCancellation(error) { await ArtworkDiagnostics.shared.record(url.host ?? "image", error.localizedDescription) }
                    return nil
                }
            }
            inflight[key] = Request(id: requestID, task: task, subscribers: [subscriber])
        }
        return await withTaskCancellationHandler(operation: {
            let result = await task.value
            guard !Task.isCancelled, let result else {
                if !Task.isCancelled { failedCount += 1; recordFailure(key) }
                unsubscribe(key, subscriber); return nil
            }
            loadedCount += 1
            let image = NSImage(cgImage: result, size: NSSize(width: result.width, height: result.height))
            ArtworkMemory.shared.store(image, forKey: key, cost: result.bytesPerRow * result.height)
            if inflight[key]?.id == requestID { inflight.removeValue(forKey: key) }
            return image
        }, onCancel: { Task { @MainActor in self.unsubscribe(key, subscriber) } })
    }
}
struct Artwork: View {
    var address: String?; var width: Int = 400; var fill = true
    /// SF Symbol shown faintly while the picture is missing (film, tv, gamecontroller).
    var placeholderSymbol: String? = nil
    var fallbackAddress: String? = nil
    @State private var image: NSImage?
    @State private var usingFallback = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    init(address: String?, width: Int = 400, fill: Bool = true, placeholderSymbol: String? = nil, fallbackAddress: String? = nil) {
        self.address = address; self.width = width; self.fill = fill; self.placeholderSymbol = placeholderSymbol
        self.fallbackAddress = fallbackAddress
        _image = State(initialValue: ImageStore.cachedImage(address, width: width))
    }
    private var fade: Animation? { reduceMotion ? nil : Animation.easeOut(duration: 0.2) }
    private var hasImage: Bool { image != nil }
    var body: some View {
        ZStack {
            Color.primary.opacity(0.06)
            if image == nil, let placeholderSymbol {
                Image(systemName: placeholderSymbol).font(.system(size: 30)).foregroundStyle(Color.secondary).opacity(0.3)
            }
            if let image {
                Image(nsImage: image).resizable().aspectRatio(contentMode: fill && !usingFallback ? .fill : .fit).transition(.opacity)
            }
        }
        .animation(fade, value: hasImage)
        .clipped()
        .accessibilityHidden(true)
        .onAppear { if image == nil, let ready = ImageStore.cachedImage(address, width: width) { image = ready } }
        .task(id: (address ?? "") + "|" + (fallbackAddress ?? "")) {
            usingFallback = false
            guard let address, !address.isEmpty else { image = nil; return }
            if let ready = ImageStore.cachedImage(address, width: width) { image = ready; return }
            image = nil
            var next = await ImageStore.shared.image(address, width: width)
            var fallback = false
            if next == nil, !Task.isCancelled, let fallbackAddress, fallbackAddress != address {
                next = await ImageStore.shared.image(fallbackAddress, width: width)
                fallback = next != nil
            }
            if !Task.isCancelled { usingFallback = fallback; image = next }
        }
        .onDisappear { image = nil }
    }
}
