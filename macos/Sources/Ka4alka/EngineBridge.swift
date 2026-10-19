import AppKit
import Foundation

@MainActor final class EngineBridge {
    private var process: Process?
    private var input: Pipe?
    private var outputReader: FileHandle?
    private var pending: [String: CheckedContinuation<JSONValue, Error>] = [:]
    private var deadlines: [String: Task<Void, Never>] = [:]
    private let stderrTail = StderrTail()
    private var expectedExit = false
    var onEvent: ((String, JSONValue) -> Void)?
    var onFailure: ((String) -> Void)?
    /// Called when the engine process ends without us asking for it (crash, kill). Not called after `shutdown()`.
    var onExit: ((Int32, Process.TerminationReason) -> Void)?
    /// Offline layout tests: every call fails with code "disabled" and no process is ever launched.
    var disabled = false

    func start() throws {
        if disabled { throw RPCError(code: "disabled", message: tr("Движок отключён.", "The engine is disabled.")) }
        if process?.isRunning == true { return }
        let engine = Bundle.main.resourceURL!.appendingPathComponent("engine/Kachalka.Engine")
        let executable = ProcessInfo.processInfo.environment["KACHALKA_ENGINE_PATH"].map { URL(fileURLWithPath: $0) } ?? engine
        guard FileManager.default.isExecutableFile(atPath: executable.path) else { throw RPCError(code: "missing-engine", message: tr("Не найден движок приложения. Переустанови Качалку.", "The application engine is missing. Reinstall Ka4alka.")) }
        let process = Process(); let output = Pipe(); let errors = Pipe(); let input = Pipe()
        process.executableURL = executable; process.standardInput = input; process.standardOutput = output; process.standardError = errors
        var environment = ProcessInfo.processInfo.environment
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        process.environment = environment
        // A write to a dead engine must fail with an error, never raise SIGPIPE.
        signal(SIGPIPE, SIG_IGN)
        let tail = stderrTail; tail.reset(); expectedExit = false
        errors.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            if data.isEmpty { handle.readabilityHandler = nil; return }
            tail.append(data)
        }
        process.terminationHandler = { [weak self] finished in
            let identity = ObjectIdentifier(finished); let status = finished.terminationStatus; let reason = finished.terminationReason
            Task { @MainActor in self?.ended(identity, status, reason) }
        }
        try process.run(); self.input = input; self.process = process
        // FileHandle.AsyncBytes may perform a blocking read on the caller's
        // executor for pipes. Read available chunks on Foundation's I/O queue;
        // only completed messages are delivered to the main actor.
        let decoder = PipeMessageDecoder(onMessage: { [weak self] data in
            Task { @MainActor in self?.receive(data) }
        }, onError: { [weak self] message in
            Task { @MainActor in self?.onFailure?(message) }
        })
        outputReader = output.fileHandleForReading
        output.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            if data.isEmpty { handle.readabilityHandler = nil; return }
            decoder.consume(data)
        }
    }
    func call(_ method: String, _ parameters: [String: JSONValue] = [:]) async throws -> JSONValue {
        if disabled { throw RPCError(code: "disabled", message: tr("Движок отключён.", "The engine is disabled.")) }
        try start(); let id = UUID().uuidString
        return try await withTaskCancellationHandler(operation: {
            try Task.checkCancellation()
            return try await withCheckedThrowingContinuation { continuation in
                pending[id] = continuation
                do {
                    try send(id: id, method: method, parameters: parameters)
                    deadlines[id] = Task { [weak self] in
                        try? await Task.sleep(for: .seconds(method == "prepareUpdate" ? 1200 : 95))
                        guard !Task.isCancelled else { return }
                        try? self?.send(id: UUID().uuidString, method: "cancel", parameters: ["id": .string(id)])
                        self?.finish(id, .failure(RPCError(code: "timeout", message: tr("Источник не ответил вовремя.", "The source did not respond in time."))))
                    }
                } catch { finish(id, .failure(error)) }
            }
        }, onCancel: { [weak self] in Task { @MainActor in self?.cancel(id) } })
    }
    func call<T: Decodable>(_ method: String, _ parameters: [String: JSONValue] = [:], as: T.Type) async throws -> T { try await call(method, parameters).decode(T.self) }
    private func send(id: String, method: String, parameters: [String: JSONValue]) throws {
        let value = JSONValue.object(["id": .string(id), "method": .string(method), "params": .object(parameters)])
        var data = try JSONEncoder().encode(value); data.append(10)
        try input?.fileHandleForWriting.write(contentsOf: data)
    }
    private func receive(_ data: Data) {
        guard let envelope = try? JSONDecoder().decode(Envelope.self, from: data) else { return }
        if let event = envelope.event { onEvent?(event, envelope.data ?? .null); return }
        guard let id = envelope.id else { return }
        if let error = envelope.error { finish(id, .failure(error)) } else { finish(id, .success(envelope.result ?? .null)) }
    }
    private func finish(_ id: String, _ result: Result<JSONValue, Error>) {
        deadlines.removeValue(forKey: id)?.cancel(); pending.removeValue(forKey: id)?.resume(with: result)
    }
    private func cancel(_ id: String) {
        guard pending[id] != nil else { return }
        try? send(id: UUID().uuidString, method: "cancel", parameters: ["id": .string(id)])
        finish(id, .failure(CancellationError()))
    }
    private func ended(_ identity: ObjectIdentifier, _ status: Int32, _ reason: Process.TerminationReason) {
        // A late notification of an older process must not tear down its successor.
        guard let current = process, ObjectIdentifier(current) == identity else { return }
        process = nil; input = nil
        outputReader?.readabilityHandler = nil; outputReader = nil
        let tail = stderrTail.text(limit: 400)
        let base = tr("Движок завершил работу.", "The engine stopped.")
        let message = tail.isEmpty ? base : base + "\n" + tail
        for id in Array(pending.keys) { finish(id, .failure(RPCError(code: "engine-exit", message: message))) }
        if !expectedExit { onExit?(status, reason) }
    }
    func shutdown() async {
        guard let process, process.isRunning else { return }
        expectedExit = true
        let shutdown = Task { _ = try? await call("shutdown") }
        let timeout = Task { try? await Task.sleep(for: .seconds(15)); if !Task.isCancelled { shutdown.cancel(); process.terminate() } }
        await shutdown.value; timeout.cancel()
        try? input?.fileHandleForWriting.close()
        outputReader?.readabilityHandler = nil
    }
}

// One pipe can split a UTF-8 character, JSON string or newline at any byte.
// Decode only complete lines and cap the buffered response before allocating
// more unbounded data. This class never calls into AppKit synchronously.
final class PipeMessageDecoder: @unchecked Sendable {
    private let lock = NSLock()
    private var buffer = Data()
    private var discarding = false
    private let limit: Int
    private let onMessage: @Sendable (Data) -> Void
    private let onError: @Sendable (String) -> Void
    init(limit: Int = 8 * 1024 * 1024, onMessage: @escaping @Sendable (Data) -> Void, onError: @escaping @Sendable (String) -> Void) {
        self.limit = limit; self.onMessage = onMessage; self.onError = onError
    }
    private func oversized() -> String { tr("Ответ движка слишком большой и был пропущен.", "An engine response was too large and was skipped.") }
    /// Returns false when an oversized message was dropped. The decoder stays usable: it skips to the next newline and continues.
    @discardableResult func consume(_ data: Data) -> Bool {
        lock.lock(); defer { lock.unlock() }
        var healthy = true
        buffer.append(data)
        while let newline = buffer.firstIndex(of: 10) {
            let line = Data(buffer[..<newline]); buffer.removeSubrange(...newline)
            if discarding { discarding = false; continue }
            if line.count > limit { onError(oversized()); healthy = false; continue }
            if !line.isEmpty { onMessage(line) }
        }
        if discarding { buffer.removeAll() }
        else if buffer.count > limit { buffer.removeAll(); discarding = true; onError(oversized()); healthy = false }
        return healthy
    }
}
// The last few KB the engine wrote to stderr, kept for the exit message.
final class StderrTail: @unchecked Sendable {
    private let lock = NSLock()
    private var bytes = Data()
    private let capacity = 8 * 1024
    func append(_ data: Data) {
        lock.lock(); defer { lock.unlock() }
        bytes.append(data)
        if bytes.count > capacity { bytes = Data(bytes.suffix(capacity)) }
    }
    func reset() { lock.lock(); bytes.removeAll(); lock.unlock() }
    func text(limit: Int) -> String {
        lock.lock(); defer { lock.unlock() }
        let value = String(decoding: bytes, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
        return String(value.suffix(limit))
    }
}
