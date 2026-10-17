import BackgroundTasks
import SwiftUI

@main
struct EADCompanionApp: App {
    @StateObject private var model = AppModel()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(model)
                .onOpenURL { url in
                    model.handle(url: url)
                }
        }
        .onChange(of: scenePhase) { _, phase in
            switch phase {
            case .active:
                model.refresh()
                Task { await model.retryAll() }
            case .background:
                BackgroundRetry.scheduleIfNeeded()
            default:
                break
            }
        }
        .backgroundTask(.appRefresh(AppConfig.backgroundTaskID)) {
            _ = await OutboxSender.shared.sendAllPending()
            BackgroundRetry.scheduleIfNeeded()
        }
    }
}

/// iOS may give the app a few seconds now and then while it is in the background. This asks for one whenever photos
/// are still waiting, so a share made without a connection goes out later on its own. iOS decides when, and whether;
/// opening the app always retries straight away.
enum BackgroundRetry {
    static func scheduleIfNeeded() {
        guard BatchStore.shared.all().contains(where: { !$0.isDone }) else { return }
        let request = BGAppRefreshTaskRequest(identifier: AppConfig.backgroundTaskID)
        request.earliestBeginDate = Date(timeIntervalSinceNow: 15 * 60)
        try? BGTaskScheduler.shared.submit(request)
    }
}
