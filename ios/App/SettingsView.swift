import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var model: AppModel

    @State private var smtp = SmtpSettings()
    @State private var options = ImageOptions()
    @State private var password = ""
    @State private var result: String?
    @State private var testing = false
    @State private var loaded = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Server", text: $smtp.host)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.URL)
                    TextField("Port", text: Binding(
                        get: { String(smtp.port) },
                        set: { smtp.port = Int($0.filter(\.isNumber)) ?? smtp.port })
                    )
                    .keyboardType(.numberPad)
                    Picker("Security", selection: $smtp.security) {
                        ForEach(SmtpSettings.Security.allCases) { security in
                            Text(security.label).tag(security)
                        }
                    }
                    TextField("User name", text: $smtp.username)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.emailAddress)
                        .textContentType(.username)
                    SecureField(model.hasPassword ? "Password (leave blank to keep)" : "Password", text: $password)
                        .textContentType(.password)
                    TextField("From address (optional)", text: $smtp.fromAddress)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.emailAddress)
                } header: {
                    Text("Mail account to send through")
                } footer: {
                    Text("Photos are e-mailed to your PC's mailbox from this account. For Gmail use smtp.gmail.com, port 465, your address, and an app password (Google Account, Security, 2-Step Verification, App passwords). The password is kept in the iPhone keychain.")
                }

                Section("Photos") {
                    Toggle("Send iPhone photos as JPEG", isOn: $options.convertHEICToJPEG)
                    Picker("Longest side", selection: $options.maxDimension) {
                        Text("Original size").tag(0)
                        Text("3840 px (4K)").tag(3840)
                        Text("2560 px").tag(2560)
                        Text("1920 px (Full HD)").tag(1920)
                    }
                }

                Section {
                    Button {
                        Task { await test() }
                    } label: {
                        if testing { ProgressView() } else { Text("Test sign-in") }
                    }
                    .disabled(testing)

                    if let result {
                        Text(result).font(.footnote)
                    }

                    Button("Save") {
                        if model.saveSettings(smtp: smtp, password: password, options: options) {
                            password = ""
                            result = "Saved."
                        }
                    }
                    .bold()
                }
            }
            .navigationTitle("Settings")
            .onAppear {
                guard !loaded else { return }
                loaded = true
                smtp = model.config.smtp
                options = model.config.options
            }
        }
    }

    private func test() async {
        testing = true
        result = await model.testLogin(smtp: smtp, password: password)
        testing = false
    }
}
