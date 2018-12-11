package com.horine.emailAttachmentDownloader;

import javax.swing.*;
import javax.swing.border.Border;
import javax.swing.event.MenuDragMouseEvent;
import javax.swing.event.MenuDragMouseListener;
import javax.swing.filechooser.FileFilter;
import javax.swing.filechooser.FileNameExtensionFilter;
import java.awt.*;
import java.awt.event.ActionEvent;
import java.awt.event.ActionListener;
import java.awt.event.MouseEvent;
import java.awt.event.MouseListener;
import java.io.File;
import java.io.FileOutputStream;
import java.util.ArrayList;

public class DisplayPage {

    ArrayList<DisplayElem> elements;
    JPanel elementPanel;
    JFrame frame;
    GlobalSettings settings;
    EmailGetter getter;

    public DisplayPage(GlobalSettings settings, EmailGetter getter) {
        this.settings = settings;
        this.getter = getter;

        frame = new JFrame("Attachment Downloader");
        frame.setDefaultCloseOperation(JFrame.HIDE_ON_CLOSE);
        frame.setSize(400,200);
        frame.setLayout(new BorderLayout());
        frame.setJMenuBar(generateMenu());
        elements = new ArrayList<DisplayElem>();
        JPanel elementPanel = new JPanel();
        frame.add(elementPanel, BorderLayout.CENTER);
        frame.setVisible(true);
    }

    public void update(){
        elementPanel = new JPanel();
        for (int i = 0; i < elements.size(); i++){

        }
    }

    public void addElement(DisplayElem element){
        elements.add(element);

    }

    public String chooseFolder(){
        String filepath;

        JFileChooser fileChooser = new JFileChooser();
        fileChooser.setFileSelectionMode(JFileChooser.DIRECTORIES_ONLY);
        fileChooser.showOpenDialog(frame);
        File selectedfile = fileChooser.getSelectedFile();
        filepath = selectedfile.getAbsolutePath();
        return filepath;
    }

    public String chooseFile(String filename, String description, String extensions){
        String filepath;

        JFileChooser fileChooser = new JFileChooser();
        fileChooser.setFileSelectionMode(JFileChooser.FILES_ONLY);
        fileChooser.setSelectedFile(new File(filename));
        FileNameExtensionFilter filter = new FileNameExtensionFilter(description, extensions);
        fileChooser.setFileFilter(filter);
        fileChooser.showOpenDialog(frame);
        File selectedfile = fileChooser.getSelectedFile();
        filepath = selectedfile.getAbsolutePath();
        return filepath;
    }

    private JMenuBar generateMenu(){
        JMenuBar menubar = new JMenuBar();

        //===FILE MENU====
        menubar.add(generateFileMenu());

        //===RUN MENU====
        JMenu runMenu = new JMenu("Run");
        menubar.add(runMenu);

        JMenuItem run = new JMenuItem("Run Now");
        run.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                getter.fetch(settings.getKeywords());
            }
        });
        //JCheckBoxMenuItem startup = new JCheckBoxMenuItem("Run at Startup");
        runMenu.add(run);

        //===HELP MENU====
        JMenu helpMenu = new JMenu("Help");

        JMenuItem help = new JMenuItem("Help");
        JMenuItem gettingStarted = new JMenuItem("Getting Started");
        JMenuItem faq = new JMenuItem("FAQ");
        JMenuItem about = new JMenuItem("About this Software");

        helpMenu.add(help);
        helpMenu.add(gettingStarted);
        helpMenu.add(faq);
        helpMenu.addSeparator();
        helpMenu.add(about);

        menubar.add(helpMenu);

        return menubar;
    }

    private JMenu generateFileMenu(){
        JMenu filemenu = new JMenu("File");

        //===IMPORT SETTINGS====
        JMenuItem importSettings = new JMenuItem("Import Settings");
        importSettings.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                String file = chooseFile("settings.cfg",".cfg", "cfg");
                if (file != null) {
                    String oldPath = settings.getCfgFilepath();
                    settings.setCfgFilepath(file);
                    settings.getData();
                    settings.setCfgFilepath(oldPath);
                    settings.saveData();
                }
            }});
        filemenu.add(importSettings);
        //===EXPORT SETTINGS====
        JMenuItem exportSettings = new JMenuItem("Export Settings");
        exportSettings.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                String file = chooseFile("export.cfg",".cfg", "cfg");
                if (file != null) {
                    String oldPath = settings.getCfgFilepath();
                    settings.setCfgFilepath(file);
                    settings.saveData();
                    settings.setCfgFilepath(oldPath);
                }
            }});
        filemenu.add(exportSettings);
        filemenu.addSeparator();

        //===SAVE FOLDER====
        JMenuItem saveFolder = new JMenuItem("Picture Save Folder");
        exportSettings.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                String folder = chooseFolder();
                if (folder != null) {
                    settings.setSaveFolder(folder);
                }
            }});
        filemenu.add(saveFolder);

        //===PREFRENCES====
        JMenuItem prefs = new JMenuItem("Prefrences");
        filemenu.add(prefs);
        prefs.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                updatePrefrences();
            }});

        return filemenu;
    }

    void updatePrefrences(){
        JFrame settingsEdit = new JFrame();
        settingsEdit.setDefaultCloseOperation(JFrame.DISPOSE_ON_CLOSE);
        settingsEdit.setSize(400,600);
        settingsEdit.setLocation(500,300);
        SpringLayout layout = new SpringLayout();
        settingsEdit.setLayout(layout);

        JLabel mailserverLabel = new JLabel("Mail Server: ");
        JLabel emailLabel = new JLabel("Email: ");
        JLabel passwordLabel = new JLabel("Password: ");

        JTextField mailServer = new JTextField();
        mailServer.setSize(100,30);
        mailServer.setLocation(10,10);
        mailServer.setText(settings.getPopHost());
        JTextField email = new JTextField();
        email.setSize(100,30);
        email.setLocation(10, 50);
        email.setText(settings.getAccount());
        JPasswordField password = new JPasswordField();
        password.setSize(100,30);
        password.setLocation(10, 90);
        password.setText(settings.getPassword());
        JTextPane keywords = new JTextPane();
        keywords.setSize(400,30);
        keywords.setLocation(10,130);
        //keywords.setBackground(new Color(5,5,50));
        keywords.setText(settings.getKeywordString());

        JButton save = new JButton("Save");
        save.addActionListener(new ActionListener() {
            @Override public void actionPerformed(ActionEvent e) {
                settings.setPopHost(mailServer.getText());
                settings.setAccount(email.getText());
                settings.setPassword(password.getText());
                settings.setKeywords(keywords.getText());
                settingsEdit.dispose();
            }});
        JButton cancel = new JButton("Cancel");
        cancel.addActionListener(new ActionListener() {
            @Override public void actionPerformed(ActionEvent e) {
                settingsEdit.dispose();
            }});
        settingsEdit.add(mailserverLabel);
        layout.putConstraint(SpringLayout.WEST, mailserverLabel, 5, SpringLayout.WEST, settingsEdit);
        layout.putConstraint(SpringLayout.NORTH, mailserverLabel, 20, SpringLayout.NORTH, settingsEdit);
        settingsEdit.add(mailServer);
        layout.putConstraint(SpringLayout.WEST, mailServer, 5, SpringLayout.WEST, mailserverLabel);
        layout.putConstraint(SpringLayout.NORTH, mailServer, 5, SpringLayout.NORTH, settingsEdit);
        settingsEdit.add(emailLabel);
        layout.putConstraint(SpringLayout.WEST, emailLabel, 5, SpringLayout.WEST, settingsEdit);
        layout.putConstraint(SpringLayout.NORTH, emailLabel, 5, SpringLayout.NORTH, mailserverLabel);
        settingsEdit.add(email);
        layout.putConstraint(SpringLayout.WEST, email, 5, SpringLayout.WEST, emailLabel);
        layout.putConstraint(SpringLayout.NORTH, email, 5, SpringLayout.NORTH, mailServer);
        settingsEdit.add(passwordLabel);
        layout.putConstraint(SpringLayout.WEST, passwordLabel, 5, SpringLayout.WEST, settingsEdit);
        layout.putConstraint(SpringLayout.NORTH, passwordLabel, 5, SpringLayout.NORTH, emailLabel);
        settingsEdit.add(password);
        layout.putConstraint(SpringLayout.WEST, password, 5, SpringLayout.WEST, passwordLabel);
        layout.putConstraint(SpringLayout.NORTH, password, 5, SpringLayout.NORTH, email);
        settingsEdit.add(keywords);
        layout.putConstraint(SpringLayout.WEST, keywords, 5, SpringLayout.WEST, passwordLabel);
        layout.putConstraint(SpringLayout.NORTH, keywords, 5, SpringLayout.NORTH, passwordLabel);

        settingsEdit.add(save);
        layout.putConstraint(SpringLayout.WEST, save, 5, SpringLayout.WEST, settingsEdit);
        layout.putConstraint(SpringLayout.SOUTH, save, 5, SpringLayout.SOUTH, settingsEdit);
        settingsEdit.add(cancel);
        layout.putConstraint(SpringLayout.EAST, cancel, 5, SpringLayout.WEST, save);
        layout.putConstraint(SpringLayout.SOUTH, cancel, 5, SpringLayout.SOUTH, settingsEdit);

        settingsEdit.setVisible(true);
    }
}
