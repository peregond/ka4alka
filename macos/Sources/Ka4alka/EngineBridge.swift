import AppKit
import Foundation

@MainActor final class EngineBridge {
    private var process: Process?
    private var input: Pipe?
    private var outputReader: FileHandle?
    private var pending: [String: CheckedContinuation<JSONValue, Error>] = [:]
    private var deadlines: [String: Task<Void, Never>] = [:]
    var onEvent: ((String, JSONValue) -> Void)?
    var onFailure: ((String) -> Void)?

    func start() throws {
        if process?.isRunning == true { return }
        let engine = Bundle.main.resourceURL!.appendingPathComponent("engine/Kachalka.Engine")
        let executable = ProcessInfo.processInfo.environment["KACHALKA_ENGINE_PATH"].map { URL(fileURLWithPath: $0) } ?? engine
        guard FileManager.default.isExecutableFile(atPath: executable.path) else { throw RPCError(code: "missing-engine", message: tr("Не найден движок приложения. Переустанови Качалку.", "The application engine is missing. Reinstall Ka4alka.")) }
        let process = Process(); let output = Pipe(); let errors = Pipe(); let input = Pipe()
        process.executableURL = executable; process.standardInput = input; process.standardOutput = output; process.standardError = errors
        var environment = ProcessInfo.processInfo.environment
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        process.environment = environment
        errors.fileHandleForReading.readabilityHandler = { handle in _ = handle.availableData }
        process.terminationHandler = { [weak self] _ in Task { @MainActor in self?.ended() } }
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
            if data.isEmpty || !decoder.consume(data) { handle.readabilityHandler = nil }
        }

    }
    func call(_ method: String, _ parameters: [String: JSONValue] = [:]) async throws -> JSONValue {
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
    private func ended() {
        process = nil
        for id in Array(pending.keys) { finish(id, .failure(RPCError(code: "engine-exit", message: tr("Движок завершил работу. Открой приложение заново.", "The engine stopped. Reopen the application.")))) }
    }
    func shutdown() async {
        guard let process, process.isRunning else { return }
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
    private let limit: Int
    private let onMessage: @Sendable (Data) -> Void
    private let onError: @Sendable (String) -> Void
    init(limit: Int = 8 * 1024 * 1024, onMessage: @escaping @Sendable (Data) -> Void, onError: @escaping @Sendable (String) -> Void) {
        self.limit = limit; self.onMessage = onMessage; self.onError = onError
    }
    @discardableResult func consume(_ data: Data) -> Bool {
        lock.lock(); defer { lock.unlock() }
        buffer.append(data)
        while let newline = buffer.firstIndex(of: 10) {
            let line = Data(buffer[..<newline]); buffer.removeSubrange(...newline)
            guard line.count <= limit else { buffer.removeAll(); onError("IPC response exceeds limit"); return false }
            if !line.isEmpty { onMessage(line) }
        }
        guard buffer.count <= limit else { buffer.removeAll(); onError("IPC response exceeds limit"); return false }
        return true
    }
}
