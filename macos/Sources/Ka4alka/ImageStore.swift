import AppKit
import ImageIO
import SwiftUI

actor ImageSlots {
    private var active = 0
    private var waiters: [(UUID, CheckedContinuation<Void, Error>)] = []
    func acquire() async throws {
        let id = UUID()
        try await withTaskCancellationHandler(operation: {
            try Task.checkCancellation()
            if active < 3 { active += 1; return }
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
@MainActor final class ImageStore {
    static let shared = ImageStore()
    private struct Request { var id: UUID; var task: Task<CGImage?, Never>; var subscribers: Set<UUID> }
    private let cache = NSCache<NSString, NSImage>()
    private var inflight: [String: Request] = [:]
    private let slots = ImageSlots()
    private let session: URLSession
    init() {
        cache.totalCostLimit = 32 * 1024 * 1024
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 18; config.httpMaximumConnectionsPerHost = 3
        config.urlCache = URLCache(memoryCapacity: 4 * 1024 * 1024, diskCapacity: 128 * 1024 * 1024, diskPath: "Ka4alkaArtwork")
        session = URLSession(configuration: config)
    }
    func setLite(_ lite: Bool) { cache.totalCostLimit = (lite ? 12 : 32) * 1024 * 1024 }
    func clear() { cache.removeAllObjects(); session.configuration.urlCache?.removeAllCachedResponses() }
    private func unsubscribe(_ key: String, _ subscriber: UUID) {
        guard var request = inflight[key] else { return }
        request.subscribers.remove(subscriber)
        if request.subscribers.isEmpty { request.task.cancel(); inflight.removeValue(forKey: key) }
        else { inflight[key] = request }
    }
    func image(_ address: String, width: Int = 400) async -> NSImage? {
        let key = address + "|" + String(width); let subscriber = UUID()
        if let ready = cache.object(forKey: key as NSString) { return ready }
        guard let url = URL(string: address), url.scheme == "https", !Task.isCancelled else { return nil }
        let task: Task<CGImage?, Never>; let requestID: UUID
        if var existing = inflight[key] {
            existing.subscribers.insert(subscriber); inflight[key] = existing; task = existing.task; requestID = existing.id
        } else {
            requestID = UUID()
            task = Task.detached(priority: .utility) { [slots, session] () -> CGImage? in
                do { try await slots.acquire() } catch { return nil }
                let result: CGImage?
                do {
                    try Task.checkCancellation()
                    let (bytes, response) = try await session.bytes(from: url)
                    guard let response = response as? HTTPURLResponse, response.statusCode == 200, response.expectedContentLength <= 12 * 1024 * 1024 else { await slots.release(); return nil }
                    var data = Data(); data.reserveCapacity(64 * 1024)
                    for try await byte in bytes { data.append(byte); if data.count > 12 * 1024 * 1024 { throw URLError(.dataLengthExceedsMaximum) } }
                    try Task.checkCancellation()
                    result = await Task.detached(priority: .utility) {
                        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil as CGImage? }
                        return CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: width, kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceShouldCacheImmediately: true] as CFDictionary)
                    }.value
                } catch { result = nil }
                await slots.release(); return result
            }
            inflight[key] = Request(id: requestID, task: task, subscribers: [subscriber])
        }
        return await withTaskCancellationHandler(operation: {
            let result = await task.value
            guard !Task.isCancelled, let result else { unsubscribe(key, subscriber); return nil }
            let image = NSImage(cgImage: result, size: NSSize(width: result.width, height: result.height))
            cache.setObject(image, forKey: key as NSString, cost: result.bytesPerRow * result.height)
            if inflight[key]?.id == requestID { inflight.removeValue(forKey: key) }
            return image
        }, onCancel: { Task { @MainActor in self.unsubscribe(key, subscriber) } })
    }
}
struct Artwork: View {
    var address: String?; var width: Int = 400; var fill = true
    @State private var image: NSImage?
    var body: some View {
        ZStack {
            Color.primary.opacity(0.06)
            if let image { Image(nsImage: image).resizable().aspectRatio(contentMode: fill ? .fill : .fit) }
        }.clipped().task(id: address) {
            guard let address else { image = nil; return }
            let next = await ImageStore.shared.image(address, width: width)
            if !Task.isCancelled { image = next }
        }.onDisappear { image = nil }
    }
}
