import Foundation

/// Settings and paired destinations, in a JSON file in the App Group folder so the app and the share extension agree.
final class ConfigStore {
    static let shared = ConfigStore()

    private let url: URL

    init(directory: URL = AppConfig.containerURL) {
        url = directory.appendingPathComponent("config.json")
    }

    func load() -> StoredConfig {
        guard let data = try? Data(contentsOf: url) else { return StoredConfig() }
        return (try? JSONDecoder().decode(StoredConfig.self, from: data)) ?? StoredConfig()
    }

    func save(_ config: StoredConfig) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(config).write(to: url, options: .atomic)
    }

    /// Reads the file, applies the change and writes it back, so two processes editing different settings do not undo each other.
    @discardableResult
    func update(_ change: (inout StoredConfig) -> Void) throws -> StoredConfig {
        var config = load()
        change(&config)
        try save(config)
        return config
    }
}

/// Turns arbitrary names (from other apps, possibly hostile) into plain ASCII file names that are safe on Windows.
enum FileNameSanitizer {
    private static let forbidden = Set("\\/:*?\"<>|".unicodeScalars)

    static func safe(_ name: String, fallback: String = "photo", maxLength: Int = 100) -> String {
        let last = (name as NSString).lastPathComponent
        var cleaned = ""
        for scalar in last.unicodeScalars {
            if scalar.isASCII && scalar.value >= 32 && !forbidden.contains(scalar) {
                cleaned.unicodeScalars.append(scalar)
            } else {
                cleaned.append("_")
            }
        }

        cleaned = cleaned.trimmingCharacters(in: CharacterSet(charactersIn: " ."))
        if cleaned.isEmpty || cleaned.allSatisfy({ $0 == "_" }) {
            cleaned = fallback
        }

        if cleaned.count > maxLength {
            let ext = (cleaned as NSString).pathExtension
            let stem = (cleaned as NSString).deletingPathExtension
            let room = max(1, maxLength - (ext.isEmpty ? 0 : ext.count + 1))
            cleaned = String(stem.prefix(room)) + (ext.isEmpty ? "" : "." + String(ext.prefix(8)))
        }

        return cleaned
    }
}

/// Photos waiting to be mailed, one folder per batch inside the App Group folder.
///
/// ```
/// Outbox/<batch id>/batch.json      the batch record
/// Outbox/<batch id>/files/          copies of the photos, removed as each one is sent
/// Outbox/<batch id>/sending.lock    present while one process is sending the batch
/// ```
final class BatchStore {
    static let shared = BatchStore()

    /// A lock older than this is assumed to belong to a process that was killed.
    static let staleLockSeconds: TimeInterval = 10 * 60

    let root: URL
    private let fileManager = FileManager.default

    init(root: URL = AppConfig.containerURL.appendingPathComponent("Outbox", isDirectory: true)) {
        self.root = root
    }

    func folder(for id: UUID) -> URL { root.appendingPathComponent(id.uuidString, isDirectory: true) }
    func filesFolder(for id: UUID) -> URL { folder(for: id).appendingPathComponent("files", isDirectory: true) }
    private func recordURL(for id: UUID) -> URL { folder(for: id).appendingPathComponent("batch.json") }
    private func lockURL(for id: UUID) -> URL { folder(for: id).appendingPathComponent("sending.lock") }

    func fileURL(batch: OutgoingBatch, item: OutgoingItem) -> URL {
        filesFolder(for: batch.id).appendingPathComponent(item.storedName)
    }

    /// A fresh folder for files that are still being collected, before there is a batch for them.
    func newStagingFolder() throws -> URL {
        let folder = root
            .deletingLastPathComponent()
            .appendingPathComponent("Staging", isDirectory: true)
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try fileManager.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }

    /// Moves (or copies) the staged files into a new batch and saves it. Files that cannot be taken are skipped; if none can, it throws.
    func create(destination: Destination, files: [StagedFile], move: Bool) throws -> OutgoingBatch {
        var batch = OutgoingBatch(destination: destination, items: [])
        let filesDirectory = filesFolder(for: batch.id)
        try fileManager.createDirectory(at: filesDirectory, withIntermediateDirectories: true)

        var lastFailure: Error?
        for file in files {
            let display = file.name.isEmpty ? file.url.lastPathComponent : file.name
            let stored = "\(batch.items.count + 1)-" + FileNameSanitizer.safe(display)
            let target = filesDirectory.appendingPathComponent(stored)
            do {
                if move {
                    try fileManager.moveItem(at: file.url, to: target)
                } else {
                    try fileManager.copyItem(at: file.url, to: target)
                }
                batch.items.append(OutgoingItem(fileName: display, storedName: stored))
            } catch {
                lastFailure = error
            }
        }

        guard !batch.items.isEmpty else {
            try? fileManager.removeItem(at: folder(for: batch.id))
            throw lastFailure ?? CocoaError(.fileWriteUnknown)
        }

        try save(batch)
        return batch
    }

    func save(_ batch: OutgoingBatch) throws {
        try fileManager.createDirectory(at: folder(for: batch.id), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        try encoder.encode(batch).write(to: recordURL(for: batch.id), options: .atomic)
    }

    func load(_ id: UUID) -> OutgoingBatch? {
        guard let data = try? Data(contentsOf: recordURL(for: id)) else { return nil }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try? decoder.decode(OutgoingBatch.self, from: data)
    }

    /// Newest first.
    func all() -> [OutgoingBatch] {
        let names = (try? fileManager.contentsOfDirectory(atPath: root.path)) ?? []
        return names
            .compactMap { UUID(uuidString: $0) }
            .compactMap { load($0) }
            .sorted { $0.createdAt > $1.createdAt }
    }

    func delete(_ id: UUID) {
        try? fileManager.removeItem(at: folder(for: id))
    }

    /// Forgets finished batches older than `age`, so the list does not grow for ever.
    func pruneSent(olderThan age: TimeInterval = 7 * 24 * 3600, now: Date = Date()) {
        for batch in all() where batch.isDone && now.timeIntervalSince(batch.createdAt) > age {
            delete(batch.id)
        }
    }

    /// Marks the batch as being sent by this process. False when another process already is.
    func claim(_ id: UUID) -> Bool {
        let path = lockURL(for: id).path
        if let attributes = try? fileManager.attributesOfItem(atPath: path),
           let modified = attributes[.modificationDate] as? Date,
           Date().timeIntervalSince(modified) > Self.staleLockSeconds {
            try? fileManager.removeItem(atPath: path)
        }

        let descriptor = open(path, O_CREAT | O_EXCL | O_WRONLY, 0o600)
        guard descriptor >= 0 else { return false }
        close(descriptor)
        return true
    }

    func release(_ id: UUID) {
        try? fileManager.removeItem(at: lockURL(for: id))
    }
}
