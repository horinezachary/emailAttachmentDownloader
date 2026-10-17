import SwiftUI

struct OutboxView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        NavigationStack {
            List {
                if let progress = model.progress {
                    Section { ProgressBanner(progress: progress) }
                }

                if model.batches.isEmpty {
                    Text("Nothing here. Photos you send show up in this list until they are on their way.")
                        .foregroundStyle(.secondary)
                }

                ForEach(model.batches) { batch in
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Image(systemName: batch.isDone ? "checkmark.circle.fill" : "clock.arrow.circlepath")
                                .foregroundStyle(batch.isDone ? Color.green : Color.orange)
                            Text(title(for: batch)).font(.headline)
                        }

                        Text(batch.createdAt.formatted(date: .abbreviated, time: .shortened))
                            .font(.caption)
                            .foregroundStyle(.secondary)

                        if let error = batch.lastError, !batch.isDone {
                            Text(error).font(.footnote).foregroundStyle(.red)
                        }

                        if !batch.isDone {
                            Button("Try again") {
                                Task { await model.retry(batch) }
                            }
                            .buttonStyle(.bordered)
                            .disabled(model.isSending)
                        }
                    }
                    .swipeActions {
                        Button("Delete", role: .destructive) {
                            model.delete(batch)
                        }
                    }
                }
            }
            .navigationTitle("Outbox")
            .toolbar {
                if model.unfinishedCount > 0 {
                    ToolbarItem(placement: .primaryAction) {
                        Button("Send all") { Task { await model.retryAll() } }
                            .disabled(model.isSending)
                    }
                }
            }
            .refreshable { model.refresh() }
        }
    }

    private func title(for batch: OutgoingBatch) -> String {
        let total = batch.items.count
        let photos = total == 1 ? "1 photo" : "\(total) photos"
        if batch.isDone {
            return "\(photos) sent to \(batch.destination.name)"
        }
        return "\(batch.unsentCount) of \(photos) waiting for \(batch.destination.name)"
    }
}
