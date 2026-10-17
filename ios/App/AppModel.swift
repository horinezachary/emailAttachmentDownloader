import Foundation
import SwiftUI

/// What the screens show and do. Everything lives in the shared stores, so the share extension and the Shortcuts action see
/// the same settings and the same outbox.
@MainActor
final class AppModel: ObservableObject {
    @Published var config: StoredConfig
    @Published var batches: [OutgoingBatch] = []
    @Published var hasPassword = false
    @Published var progress: SendProgress?
    @Published var message: String?
    @Published var pendingPairing: Destination?

    private let configStore = ConfigStore.shared
    private let batchStore = BatchStore.shared
    private let sender = OutboxSender.shared

    init() {
        config = ConfigStore.shared.load()
        BatchStore.shared.pruneSent()
        refresh()
    }

    var isSending: Bool { progress != nil }

    func refresh() {
        config = configStore.load()
        batches = batchStore.all()
        hasPassword = PasswordStore.load(account: config.smtp.username.trimmed) != nil
    }

    var unfinishedCount: Int { batches.filter { !$0.isDone }.count }

    // MARK: pairing

    /// Handles an `ead://pair?...` link, from the camera, a QR scan or the paste box.
    func handle(url: URL) {
        handle(pairingText: url.absoluteString)
    }

    func handle(pairingText: String) {
        do {
            pendingPairing = try Pairing.parse(pairingText)
        } catch {
            message = error.localizedDescription
        }
    }

    func confirmPairing(_ destination: Destination) {
        do {
            try configStore.update { config in
                // Pairing the same account again (same tag) replaces it instead of adding a second entry.
                var stored = destination
                if let existing = config.destinations.firstIndex(where: { $0.tag == destination.tag }) {
                    stored.id = config.destinations[existing].id
                    config.destinations[existing] = stored
                } else {
                    config.destinations.append(stored)
                }
                config.lastDestinationID = config.lastDestinationID ?? stored.id
            }
        } catch {
            message = "Could not save: \(error.localizedDescription)"
        }
        pendingPairing = nil
        refresh()
    }

    func removeDestinations(at offsets: IndexSet) {
        let ids = offsets.map { config.destinations[$0].id }
        do {
            try configStore.update { config in
                config.destinations.removeAll { ids.contains($0.id) }
                if let last = config.lastDestinationID, ids.contains(last) {
                    config.lastDestinationID = nil
                }
            }
        } catch {
            message = "Could not save: \(error.localizedDescription)"
        }
        refresh()
    }

    // MARK: settings

    func saveSettings(smtp: SmtpSettings, password: String, options: ImageOptions) -> Bool {
        do {
            let account = smtp.username.trimmed
            if !password.isEmpty {
                try PasswordStore.save(password, account: account)
            }
            try configStore.update { config in
                config.smtp = smtp
                config.options = options
            }
            refresh()
            return true
        } catch {
            message = error.localizedDescription
            return false
        }
    }

    func testLogin(smtp: SmtpSettings, password: String) async -> String {
        let secret = password.isEmpty ? (PasswordStore.load(account: smtp.username.trimmed) ?? "") : password
        guard smtp.isConfigured, !secret.isEmpty else {
            return "Enter the server, user name and password first."
        }
        if let problem = await sender.testLogin(settings: smtp, password: secret) {
            return "Sign-in failed: \(problem)"
        }
        return "Signed in to \(smtp.host.trimmed) successfully."
    }

    // MARK: sending

    /// Queues the photos and tries to send them straight away. They stay in the outbox if that fails.
    func send(files: [StagedFile], to destination: Destination) async {
        guard !files.isEmpty else { return }
        progress = SendProgress(text: "Getting ready", fraction: 0)
        defer {
            progress = nil
            refresh()
        }

        do {
            try configStore.update { $0.lastDestinationID = destination.id }
            let batch = try batchStore.create(destination: destination, files: files, move: true)
            refresh()
            let outcome = await sender.send(batchID: batch.id) { [weak self] update in
                Task { @MainActor in self?.progress = update }
            }
            message = Self.summary(outcome, count: files.count, destination: destination)
        } catch {
            message = "Could not queue the photos: \(error.localizedDescription)"
        }
    }

    func retry(_ batch: OutgoingBatch) async {
        progress = SendProgress(text: "Getting ready", fraction: 0)
        defer {
            progress = nil
            refresh()
        }
        let outcome = await sender.send(batchID: batch.id) { [weak self] update in
            Task { @MainActor in self?.progress = update }
        }
        message = Self.summary(outcome, count: batch.items.count, destination: batch.destination)
    }

    func retryAll() async {
        guard !isSending, unfinishedCount > 0 else { return }
        progress = SendProgress(text: "Sending what is waiting", fraction: 0)
        defer {
            progress = nil
            refresh()
        }
        _ = await sender.sendAllPending { [weak self] update in
            Task { @MainActor in self?.progress = update }
        }
    }

    func delete(_ batch: OutgoingBatch) {
        batchStore.delete(batch.id)
        refresh()
    }

    private static func summary(_ outcome: SendOutcome, count: Int, destination: Destination) -> String {
        let photos = count == 1 ? "1 photo" : "\(count) photos"
        switch outcome {
        case .sent:
            return "Sent \(photos) to \(destination.name)."
        case .partial(let reason):
            return "Some photos were sent. The rest are waiting in the Outbox: \(reason)"
        case .failed(let reason):
            return "Not sent. The photos are waiting in the Outbox: \(reason)"
        case .notConfigured:
            return outcome.message
        case .busy:
            return "These photos are already being sent."
        }
    }
}
