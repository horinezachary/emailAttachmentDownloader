import Foundation
import SwiftUI
import UIKit
import UniformTypeIdentifiers

enum ShareError: LocalizedError {
    case unreadable

    var errorDescription: String? {
        "That item could not be read as a picture."
    }
}

@MainActor
final class ShareModel: ObservableObject {
    enum Phase: Equatable {
        case loading
        case ready
        case sending(Double, String)
        case done(String)
        case problem(String)
        case needsSetup(String)
        case nothingToSend
    }

    @Published var phase: Phase = .loading
    @Published var destinations: [Destination] = []
    @Published var selectedID: UUID?
    @Published var photoCount = 0

    private let items: [NSExtensionItem]
    private let finish: (Error?) -> Void
    private var staged: [StagedFile] = []
    private var stagingFolder: URL?
    private var sentBatchID: UUID?

    init(items: [NSExtensionItem], finish: @escaping (Error?) -> Void) {
        self.items = items
        self.finish = finish
        Task { await load() }
    }

    var selectedDestination: Destination? {
        destinations.first { $0.id == selectedID } ?? destinations.first
    }

    // MARK: loading

    private func load() async {
        let config = ConfigStore.shared.load()
        destinations = config.destinations
        selectedID = config.preferredDestination?.id

        guard !config.destinations.isEmpty else {
            phase = .needsSetup("No frame is paired yet. Open the Photo Frame app and scan the pairing code from your PC.")
            return
        }
        guard config.smtp.isConfigured, PasswordStore.load(account: config.smtp.username.trimmed) != nil else {
            phase = .needsSetup("The mail account is not set up. Open the Photo Frame app, go to Settings and enter it.")
            return
        }

        do {
            let folder = try BatchStore.shared.newStagingFolder()
            stagingFolder = folder
            let providers = items.flatMap { $0.attachments ?? [] }

            for provider in providers {
                if let file = try? await stage(provider, into: folder) {
                    staged.append(file)
                }
            }
        } catch {
            phase = .problem("Could not prepare the photos: \(error.localizedDescription)")
            return
        }

        photoCount = staged.count
        phase = staged.isEmpty ? .nothingToSend : .ready
    }

    /// Copies one shared item into the staging folder. The system deletes the file it hands us as soon as the handler returns,
    /// so the copy has to happen inside it.
    private func stage(_ provider: NSItemProvider, into folder: URL) async throws -> StagedFile {
        if let identifier = provider.registeredTypeIdentifiers.first(where: { UTType($0)?.conforms(to: .image) == true }) {
            return try await withCheckedThrowingContinuation { continuation in
                provider.loadFileRepresentation(forTypeIdentifier: identifier) { url, error in
                    guard let url else {
                        continuation.resume(throwing: error ?? ShareError.unreadable)
                        return
                    }

                    let suggested = provider.suggestedName ?? ""
                    let ext = url.pathExtension
                    let display = suggested.isEmpty
                        ? url.lastPathComponent
                        : (ext.isEmpty || suggested.lowercased().hasSuffix("." + ext.lowercased()) ? suggested : suggested + "." + ext)
                    let target = folder.appendingPathComponent(UUID().uuidString + "-" + FileNameSanitizer.safe(display))

                    do {
                        try FileManager.default.copyItem(at: url, to: target)
                        continuation.resume(returning: StagedFile(url: target, name: display))
                    } catch {
                        continuation.resume(throwing: error)
                    }
                }
            }
        }

        // Some apps (markup, screenshots) offer only an in-memory image.
        if provider.canLoadObject(ofClass: UIImage.self) {
            return try await withCheckedThrowingContinuation { continuation in
                _ = provider.loadObject(ofClass: UIImage.self) { object, error in
                    guard let image = object as? UIImage, let data = image.jpegData(compressionQuality: 0.9) else {
                        continuation.resume(throwing: error ?? ShareError.unreadable)
                        return
                    }

                    let name = (provider.suggestedName ?? "image") + ".jpg"
                    let target = folder.appendingPathComponent(UUID().uuidString + "-" + FileNameSanitizer.safe(name))
                    do {
                        try data.write(to: target)
                        continuation.resume(returning: StagedFile(url: target, name: name))
                    } catch {
                        continuation.resume(throwing: error)
                    }
                }
            }
        }

        throw ShareError.unreadable
    }

    // MARK: actions

    func send() async {
        guard let destination = selectedDestination, !staged.isEmpty else { return }
        phase = .sending(0, "Getting ready")

        do {
            try ConfigStore.shared.update { $0.lastDestinationID = destination.id }
            let batch = try BatchStore.shared.create(destination: destination, files: staged, move: true)
            sentBatchID = batch.id
            staged = []

            let outcome = await OutboxSender.shared.send(batchID: batch.id) { [weak self] update in
                Task { @MainActor in self?.phase = .sending(update.fraction, update.text) }
            }

            switch outcome {
            case .sent:
                phase = .done("Sent to \(destination.name)")
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                close()
            case .partial(let reason), .failed(let reason):
                phase = .problem("Not sent yet. The photos are saved in the app's Outbox and will be tried again. \(reason)")
            case .notConfigured:
                phase = .needsSetup(outcome.message)
            case .busy:
                phase = .problem("These photos are already being sent.")
            }
        } catch {
            phase = .problem("Could not queue the photos: \(error.localizedDescription)")
        }
    }

    func cancel() {
        close(error: NSError(domain: NSCocoaErrorDomain, code: NSUserCancelledError))
    }

    func close(error: Error? = nil) {
        if let stagingFolder {
            try? FileManager.default.removeItem(at: stagingFolder)
        }
        finish(error)
    }
}
