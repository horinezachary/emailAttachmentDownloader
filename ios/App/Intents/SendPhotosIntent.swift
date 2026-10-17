import AppIntents
import Foundation
import UniformTypeIdentifiers

/// A paired frame, as Shortcuts shows it in the "Frame" parameter.
struct FrameEntity: AppEntity {
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Frame"
    static var defaultQuery = FrameQuery()

    var id: UUID
    var name: String

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: "\(name)")
    }
}

struct FrameQuery: EntityQuery {
    func entities(for identifiers: [UUID]) async throws -> [FrameEntity] {
        ConfigStore.shared.load().destinations
            .filter { identifiers.contains($0.id) }
            .map { FrameEntity(id: $0.id, name: $0.name) }
    }

    func suggestedEntities() async throws -> [FrameEntity] {
        ConfigStore.shared.load().destinations.map { FrameEntity(id: $0.id, name: $0.name) }
    }
}

enum SendIntentError: Error, CustomLocalizedStringResourceConvertible {
    case noFrame
    case notConfigured
    case nothingToSend
    case failed(String)

    var localizedStringResource: LocalizedStringResource {
        switch self {
        case .noFrame:
            return "Pair a frame in the Photo Frame app first."
        case .notConfigured:
            return "Set up the mail account in the Photo Frame app first."
        case .nothingToSend:
            return "There were no photos to send."
        case .failed(let reason):
            return "\(reason)"
        }
    }
}

/// "Send Photos to a Frame": usable from Shortcuts, Siri, Spotlight, the Action Button, or as a shortcut that shows in the
/// share sheet. It runs inside the app's process without opening it and uses the same outbox as the share extension, so a
/// failed send waits there and is retried.
struct SendPhotosIntent: AppIntent {
    static var title: LocalizedStringResource = "Send Photos to a Frame"
    static var description = IntentDescription(
        "E-mails photos to a Phone share account of Email Attachment Downloader so they are saved on your PC.",
        categoryName: "Photos")
    static var openAppWhenRun: Bool = false

    @Parameter(title: "Photos", supportedContentTypes: [.image])
    var photos: [IntentFile]

    @Parameter(title: "Frame")
    var frame: FrameEntity?

    static var parameterSummary: some ParameterSummary {
        Summary("Send \(\.$photos) to \(\.$frame)")
    }

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let store = ConfigStore.shared
        let config = store.load()

        guard !config.destinations.isEmpty else { throw SendIntentError.noFrame }
        guard config.smtp.isConfigured, PasswordStore.load(account: config.smtp.username.trimmed) != nil else {
            throw SendIntentError.notConfigured
        }

        let destination = config.destinations.first { $0.id == frame?.id } ?? config.preferredDestination
        guard let destination else { throw SendIntentError.noFrame }

        let batches = BatchStore.shared
        let folder = try batches.newStagingFolder()
        defer { try? FileManager.default.removeItem(at: folder) }

        var staged: [StagedFile] = []
        for (index, photo) in photos.enumerated() {
            let name = photo.filename.isEmpty ? "photo-\(index + 1).jpg" : photo.filename
            let target = folder.appendingPathComponent("\(index + 1)-" + FileNameSanitizer.safe(name))
            if let source = photo.fileURL {
                try FileManager.default.copyItem(at: source, to: target)
            } else {
                try photo.data.write(to: target)
            }
            staged.append(StagedFile(url: target, name: name))
        }
        guard !staged.isEmpty else { throw SendIntentError.nothingToSend }

        try? store.update { $0.lastDestinationID = destination.id }
        let batch = try batches.create(destination: destination, files: staged, move: true)
        let outcome = await OutboxSender.shared.send(batchID: batch.id)

        switch outcome {
        case .sent:
            let count = staged.count == 1 ? "1 photo" : "\(staged.count) photos"
            return .result(dialog: "Sent \(count) to \(destination.name).")
        case .partial(let reason), .failed(let reason):
            throw SendIntentError.failed("Not everything was sent. The rest is waiting in the app's Outbox. \(reason)")
        case .notConfigured:
            throw SendIntentError.notConfigured
        case .busy:
            throw SendIntentError.failed("These photos are already being sent.")
        }
    }
}

/// Puts the action in Shortcuts and Spotlight with a spoken phrase, with no set-up by the person.
struct EADShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: SendPhotosIntent(),
            phrases: [
                "Send photos with \(.applicationName)",
                "Send photos to my frame with \(.applicationName)",
            ],
            shortTitle: "Send Photos",
            systemImageName: "photo.on.rectangle.angled")
    }
}
