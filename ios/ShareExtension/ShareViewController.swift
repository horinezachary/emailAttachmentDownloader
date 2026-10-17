import SwiftUI
import UIKit

/// The "Photo Frame" entry in the iOS share sheet. It shows a small SwiftUI screen, queues the shared photos in the shared
/// outbox and sends them before it closes. If sending is not possible (no connection, not set up) the photos stay in the
/// outbox and the app sends them later.
final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()

        let items = extensionContext?.inputItems.compactMap { $0 as? NSExtensionItem } ?? []
        let model = ShareModel(items: items) { [weak self] error in
            if let error {
                self?.extensionContext?.cancelRequest(withError: error)
            } else {
                self?.extensionContext?.completeRequest(returningItems: nil)
            }
        }

        let host = UIHostingController(rootView: ShareView(model: model))
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)
    }
}
