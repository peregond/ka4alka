import AppKit
import ImageIO
import SwiftUI

actor ImageSlots {
    private var active = 0
    private var waiters: [CheckedContinuation<Void, Never>] = []
    func acquire() async { if active < 3 { active += 1 } else { await withCheckedContinuation { waiters.append($0) } } }
    func release() { if waiters.isEmpty { active -= 1 } else { waiters.removeFirst().resume() } }
}
@MainActor final class ImageStore {
    static let shared = ImageStore()
    private let cache = NSCache<NSString, NSImage>()
    private var inflight: [String: Task<NSImage?, Never>] = [:]
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
    func image(_ address: String, width: Int = 400) async -> NSImage? {
        let key = address + "|" + String(width)
        if let ready = cache.object(forKey: key as NSString) { return ready }
        if let task = inflight[key] { return await task.value }
        guard let url = URL(string: address), url.scheme == "https" else { return nil }
        let task = Task<NSImage?, Never> { [slots, session] in
            await slots.acquire()
            let result: NSImage?
            do {
                let (data, response) = try await session.data(from: url)
                guard let response = response as? HTTPURLResponse, response.statusCode == 200, data.count <= 12 * 1024 * 1024 else { await slots.release(); return nil }
                result = await Task.detached(priority: .utility) {
                    guard let source = CGImageSourceCreateWithData(data as CFData, nil), let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: width, kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceShouldCacheImmediately: true] as CFDictionary) else { return nil as NSImage? }
                    return NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
                }.value
            } catch { result = nil }
            await slots.release(); return result
        }
        inflight[key] = task
        let image = await task.value; inflight.removeValue(forKey: key)
        if let image { cache.setObject(image, forKey: key as NSString, cost: max(1, Int(image.size.width * image.size.height * 4))) }
        return image
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
        }
    }
}
