import Foundation

struct SendProgress {
    var text: String
    var fraction: Double
}

enum SendOutcome: Equatable {
    /// Every photo in the batch was sent.
    case sent
    /// Some photos were sent; the rest stay in the outbox with the reason.
    case partial(String)
    /// Nothing was sent; the batch stays in the outbox with the reason.
    case failed(String)
    /// There is no mail account set up yet.
    case notConfigured
    /// Another process (the app or the extension) is already sending this batch.
    case busy

    var message: String {
        switch self {
        case .sent: return "Sent."
        case .partial(let text), .failed(let text): return text
        case .notConfigured: return "Set up the mail account in the Photo Frame app first."
        case .busy: return "Already being sent."
        }
    }
}

/// Prepares, packs and mails the photos of a batch. Runs the same in the app, the share extension and a background task.
final class OutboxSender {
    static let shared = OutboxSender()

    private let configStore: ConfigStore
    private let batches: BatchStore

    init(config: ConfigStore = .shared, batches: BatchStore = .shared) {
        configStore = config
        self.batches = batches
    }

    /// Sends every photo in the batch that has not been sent yet.
    func send(batchID: UUID, progress: ((SendProgress) -> Void)? = nil) async -> SendOutcome {
        await Task.detached(priority: .userInitiated) { [self] in
            sendBlocking(batchID: batchID, progress: progress)
        }.value
    }

    /// Tries every unfinished batch, oldest first. Returns how many were completed.
    func sendAllPending(progress: ((SendProgress) -> Void)? = nil) async -> Int {
        var completed = 0
        for batch in batches.all().reversed() where !batch.isDone {
            if await send(batchID: batch.id, progress: progress) == .sent {
                completed += 1
            }
        }
        return completed
    }

    /// Checks the mail login without sending anything. Returns nil when it works, otherwise what to tell the person.
    func testLogin(settings: SmtpSettings, password: String) async -> String? {
        await Task.detached(priority: .userInitiated) {
            let session = SmtpSession(settings: settings, password: password)
            do {
                try session.connect()
                session.close()
                return nil
            } catch {
                return error.localizedDescription
            }
        }.value
    }

    // MARK: - the work

    private func sendBlocking(batchID: UUID, progress: ((SendProgress) -> Void)?) -> SendOutcome {
        let config = configStore.load()
        guard config.smtp.isConfigured,
              let password = PasswordStore.load(account: config.smtp.username.trimmed),
              !password.isEmpty
        else {
            return .notConfigured
        }

        guard var batch = batches.load(batchID) else {
            return .failed("These photos are no longer in the outbox.")
        }
        guard batches.claim(batchID) else { return .busy }
        defer { batches.release(batchID) }

        let work = batches.folder(for: batchID).appendingPathComponent("work", isDirectory: true)
        try? FileManager.default.removeItem(at: work)
        defer { try? FileManager.default.removeItem(at: work) }

        // 1. Prepare: HEIC to JPEG, shrink very large photos.
        var prepared: [(index: Int, image: PreparedImage)] = []
        let toPrepare = batch.items.indices.filter { batch.items[$0].state != .sent }
        for (position, index) in toPrepare.enumerated() {
            let item = batch.items[index]
            progress?(SendProgress(text: "Preparing \(position + 1) of \(toPrepare.count)", fraction: 0.4 * Double(position) / Double(max(toPrepare.count, 1))))
            do {
                let image = try ImagePreparer.prepare(
                    source: batches.fileURL(batch: batch, item: item),
                    displayName: item.fileName,
                    options: config.options,
                    into: work)

                if image.byteCount > MessagePlanner.maxSingleBytes {
                    batch.items[index].state = .failed
                    batch.items[index].error = "Too large to e-mail. Turn on resizing in Settings."
                } else {
                    prepared.append((index, image))
                }
            } catch {
                batch.items[index].state = .failed
                batch.items[index].error = error.localizedDescription
            }
        }

        // 2. Connect once and send the photos in as few messages as the server's size limit allows.
        let session = SmtpSession(settings: config.smtp, password: password)
        defer { session.close() }
        var stopReason: String?

        if !prepared.isEmpty {
            do {
                progress?(SendProgress(text: "Connecting to \(config.smtp.host.trimmed)", fraction: 0.4))
                try session.connect()
            } catch {
                stopReason = error.localizedDescription
            }
        }

        let groups = MessagePlanner.group(prepared, size: { $0.image.byteCount })
        for (groupIndex, group) in groups.enumerated() where stopReason == nil {
            let part = groups.count > 1 ? " (\(groupIndex + 1)/\(groups.count))" : ""
            let subject = "\(batch.destination.subjectMarker) Photos from iPhone\(part)"
            let base = 0.4 + 0.6 * Double(groupIndex) / Double(groups.count)
            let span = 0.6 / Double(groups.count)

            do {
                let message = try MimeBuilder.build(
                    from: config.smtp.effectiveFrom,
                    to: batch.destination.toAddress,
                    subject: subject,
                    bodyText: "Sent from the Email Attachment Downloader iPhone app.",
                    attachments: group.map { MimeAttachment(url: $0.image.url, fileName: $0.image.fileName, mimeType: $0.image.mimeType) },
                    into: work)

                try session.send(file: message.url, from: config.smtp.effectiveFrom, to: batch.destination.toAddress) { sent in
                    progress?(SendProgress(text: "Sending message \(groupIndex + 1) of \(groups.count)", fraction: base + span * sent))
                }

                for entry in group {
                    batch.items[entry.index].state = .sent
                    batch.items[entry.index].error = nil
                    try? FileManager.default.removeItem(at: batches.fileURL(batch: batch, item: batch.items[entry.index]))
                }
            } catch {
                for entry in group {
                    batch.items[entry.index].state = .failed
                    batch.items[entry.index].error = error.localizedDescription
                }

                // The server refusing one message (too big, a bad photo) says nothing about the next one, so carry on.
                // A dropped connection or a temporary refusal will not get better within this run, so stop and keep the rest.
                if (error as? SmtpError)?.kind != .permanent {
                    stopReason = error.localizedDescription
                }
            }
            try? batches.save(batch)
        }

        // 3. Record the result.
        let remaining = batch.unsentCount
        batch.lastError = remaining == 0 ? nil : (stopReason ?? batch.items.first(where: { $0.state == .failed })?.error)
        try? batches.save(batch)

        if remaining == 0 {
            progress?(SendProgress(text: "Sent", fraction: 1))
            return .sent
        }

        let reason = batch.lastError ?? "Some photos could not be sent."
        return remaining < batch.items.count ? .partial(reason) : .failed(reason)
    }
}
