package com.horine.emailAttachmentDownloader;

import javax.swing.*;
import javax.swing.filechooser.FileNameExtensionFilter;
import java.awt.*;
import java.awt.event.ActionEvent;
import java.awt.event.ActionListener;
import java.awt.event.MouseEvent;
import java.awt.event.MouseListener;
import java.io.File;
import java.util.ArrayList;

import static layout.SpringUtilities.makeCompactGrid;

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
        saveFolder.addMouseListener(new MouseListener(){
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
        JFrame settingsEdit = new JFrame("Preferences");
        settingsEdit.setDefaultCloseOperation(JFrame.DISPOSE_ON_CLOSE);
        settingsEdit.setSize(400,600);
        settingsEdit.setMinimumSize(new Dimension(500,190));
        settingsEdit.setLocation(500,300);
        BorderLayout layout = new BorderLayout();
        settingsEdit.setLayout(layout);

        JLabel mailserverLabel = new JLabel("Mail Server: ");
        JLabel emailLabel = new JLabel("Email: ");
        JLabel passwordLabel = new JLabel("Password: ");
        JLabel keywordsLabel = new JLabel("Keywords: ");

        JTextField mailServer = new JTextField();
        mailServer.setSize(100,30);
        mailServer.setMaximumSize(new Dimension(2000,30));
        mailServer.setLocation(10,10);
        mailServer.setText(settings.getPopHost());
        JTextField email = new JTextField();
        email.setSize(100,30);
        email.setMaximumSize(new Dimension(2000,50));
        email.setLocation(10, 50);
        email.setText(settings.getAccount());
        JPasswordField password = new JPasswordField();
        password.setSize(100,30);
        password.setMaximumSize(new Dimension(2000,50));
        password.setLocation(10, 90);
        password.setText(settings.getPassword());
        password.getBorder();
        JTextArea keywords = new JTextArea();
        keywords.setSize(400,30);
        keywords.setMinimumSize(new Dimension(30,400));
        keywords.setLocation(10,130);
        keywords.setBackground(password.getBackground());
        keywords.setBorder(password.getBorder());
        keywords.setLineWrap(true);
        keywords.setWrapStyleWord(true);


        String[] keywordstringArray = settings.getKeywords();
        String keywordString = keywordstringArray[0];
        for (int i = 1; i < keywordstringArray.length; i++){
            keywordString += ", " + keywordstringArray[i];
        }
        keywords.setText(keywordString);

        JButton save = new JButton("Save");
        save.addActionListener(new ActionListener() {
            @Override public void actionPerformed(ActionEvent e) {
                settings.setPopHost(mailServer.getText());
                settings.setAccount(email.getText());
                settings.setPassword(password.getPassword().toString());
                settings.splitKeywordString(keywords.getText());
                settingsEdit.dispose();
            }});
        JButton cancel = new JButton("Cancel");
        cancel.addActionListener(new ActionListener() {
            @Override public void actionPerformed(ActionEvent e) {
                settingsEdit.dispose();
            }});

        JPanel p = new JPanel(new SpringLayout());

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
        settingsEdit.add(p,BorderLayout.CENTER);

        JPanel south = new JPanel(new FlowLayout());
        south.add(save);
        south.add(cancel);
        south.setOpaque(true);
        settingsEdit.add(south,BorderLayout.SOUTH);
        //Display the window.
        settingsEdit.pack();
        settingsEdit.setVisible(true);
    }
}
