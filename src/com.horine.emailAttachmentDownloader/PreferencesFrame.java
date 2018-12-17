package com.horine.emailAttachmentDownloader;

import javax.swing.*;
import java.awt.*;
import java.awt.event.ActionEvent;
import java.awt.event.ActionListener;

import static layout.SpringUtilities.makeCompactGrid;

public class PreferencesFrame{

    JFrame settingsEdit;
    JTextField mailServer;
    JTextField email;
    JPasswordField password;
    JTextArea keywords;
    GlobalSettings settings;

    public PreferencesFrame(GlobalSettings settings){
        this.settings = settings;

        settingsEdit = new JFrame("Preferences");
        settingsEdit.setDefaultCloseOperation(JFrame.DISPOSE_ON_CLOSE);
        settingsEdit.setSize(400,600);
        settingsEdit.setMinimumSize(new Dimension(500,190));
        settingsEdit.setLocation(500,300);
        BorderLayout layout = new BorderLayout();
        settingsEdit.setLayout(layout);
    }

    private void setupfields(){
        mailServer = new JTextField();
        mailServer.setSize(100,30);
        mailServer.setMaximumSize(new Dimension(2000,30));
        mailServer.setLocation(10,10);
        mailServer.setText(settings.getPopHost());
        email = new JTextField();
        email.setSize(100,30);
        email.setMaximumSize(new Dimension(2000,50));
        email.setLocation(10, 50);
        email.setText(settings.getAccount());
        password = new JPasswordField();
        password.setSize(100,30);
        password.setMaximumSize(new Dimension(2000,50));
        password.setLocation(10, 90);
        password.setText(settings.getPassword());
        password.getBorder();
        keywords = new JTextArea();
        keywords.setSize(400,30);
        keywords.setMinimumSize(new Dimension(30,400));
        keywords.setLocation(10,130);
        keywords.setBackground(password.getBackground());
        keywords.setBorder(password.getBorder());
        keywords.setLineWrap(true);
        keywords.setWrapStyleWord(true);
    }

    public void updatePrefrences(){
        setupfields();
        String[] keywordstringArray = settings.getKeywords();
        String keywordString = keywordstringArray[0];
        for (int i = 1; i < keywordstringArray.length; i++){
            keywordString += ", " + keywordstringArray[i];
        }
        keywords.setText(keywordString);

        settingsEdit.add(setupcontentPanel(),BorderLayout.CENTER);
        settingsEdit.add(setupSouthPanel(),BorderLayout.SOUTH);
        //Display the window.
        settingsEdit.pack();
        settingsEdit.setVisible(true);
    }

    private JPanel setupcontentPanel(){
        JPanel p = new JPanel(new SpringLayout());

        JLabel mailserverLabel = new JLabel("Mail Server: ");
        JLabel emailLabel = new JLabel("Email: ");
        JLabel passwordLabel = new JLabel("Password: ");
        JLabel keywordsLabel = new JLabel("Keywords: ");

        mailserverLabel.setLabelFor(mailServer);
        emailLabel.setLabelFor(email);
        passwordLabel.setLabelFor(password);
        keywordsLabel.setLabelFor(keywords);

        p.add(mailserverLabel);
        p.add(mailServer);
        p.add(emailLabel);
        p.add(email);
        p.add(passwordLabel);
        p.add(password);
        p.add(keywordsLabel);
        p.add(keywords);

        makeCompactGrid(p, 4, 2,6, 6,6, 6);
        p.setOpaque(true);

        return p;
    }

    private JPanel setupSouthPanel(){
        JButton save = new JButton("Save");
        save.addActionListener(new ActionListener() {
            @Override public void actionPerformed(ActionEvent e) {
                if (mailServer.getText().equals("") || email.getText().equals("") || password.getText().equals("")) {
                    JOptionPane.showMessageDialog(settingsEdit, "Fields cannot be empty!", "Empty Fields", JOptionPane.WARNING_MESSAGE);
                }
                else {
                    settings.setPopHost(mailServer.getText());
                    settings.setAccount(email.getText());
                    settings.setPassword(password.getText());
                    settings.splitKeywordString(keywords.getText());
                    settings.saveData();
                    settingsEdit.dispose();
                }
            }});
        JButton cancel = new JButton("Cancel");
        cancel.addActionListener(new ActionListener() {
            @Override public void actionPerformed(ActionEvent e) {
                settingsEdit.dispose();
            }});

        JPanel south = new JPanel(new FlowLayout());
        south.add(save);
        south.add(cancel);
        south.setOpaque(true);
        return south;
    }
}
