import Foundation
import Security

enum KeychainError: LocalizedError {
    case failed(OSStatus)

    var errorDescription: String? {
        switch self {
        case .failed(let status):
            return "The keychain refused the password (error \(status))."
        }
    }
}

/// The mail password lives in the keychain, in a group the app and the share extension both belong to.
/// It is readable after the first unlock so a background retry can use it.
enum PasswordStore {
    private static func query(account: String) -> [String: Any] {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: AppConfig.keychainService,
            kSecAttrAccount as String: account,
        ]
        if let group = AppConfig.keychainAccessGroup {
            query[kSecAttrAccessGroup as String] = group
        }
        return query
    }

    static func save(_ password: String, account: String) throws {
        let base = query(account: account)
        let data = Data(password.utf8)

        let update: [String: Any] = [kSecValueData as String: data]
        var status = SecItemUpdate(base as CFDictionary, update as CFDictionary)

        if status == errSecItemNotFound {
            var add = base
            add[kSecValueData as String] = data
            add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
            status = SecItemAdd(add as CFDictionary, nil)
        }

        guard status == errSecSuccess else { throw KeychainError.failed(status) }
    }

    static func load(account: String) -> String? {
        var request = query(account: account)
        request[kSecReturnData as String] = true
        request[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: CFTypeRef?
        guard SecItemCopyMatching(request as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data
        else {
            return nil
        }
        return String(data: data, encoding: .utf8)
    }

    static func delete(account: String) {
        SecItemDelete(query(account: account) as CFDictionary)
    }
}
