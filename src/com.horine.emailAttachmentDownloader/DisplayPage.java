package com.horine.emailAttachmentDownloader;

import javax.swing.*;
import javax.swing.filechooser.FileNameExtensionFilter;
import java.awt.*;
import java.awt.event.MouseEvent;
import java.awt.event.MouseListener;
import java.io.File;
import java.util.ArrayList;

class DisplayPage {

    private ArrayList<DisplayElem> elements;
    private JPanel elementPanel;
    private JFrame frame;
    private ArrayList<GlobalSettings> settingsFiles;
    private MessageSaver msgSaver;

    private JMenu runMenu;
    private JMenu filemenu;

    DisplayPage(ArrayList<GlobalSettings> settingsFiles) {
        this.settingsFiles = settingsFiles;
        msgSaver = new MessageSaver();
        frame = new JFrame("Attachment Downloader");
        frame.setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
        frame.setMinimumSize(new Dimension(400,400));
        frame.setMaximumSize(new Dimension(400,800));
        frame.setPreferredSize(new Dimension(400,600));
        frame.setLocation(200,100);
        frame.setResizable(false);
        frame.setLayout(new BorderLayout());
        frame.setJMenuBar(generateMenu());
        elementPanel = new JPanel();
        elements = new ArrayList<DisplayElem>();
        msgSaver.loadElements(this);
        BoxLayout elementPanelLayout = new BoxLayout(elementPanel, BoxLayout.PAGE_AXIS);
        elementPanel.setSize(370,400);
        elementPanel.setLayout(elementPanelLayout);
        JScrollPane scrollPane = new JScrollPane(elementPanel, JScrollPane.VERTICAL_SCROLLBAR_ALWAYS, JScrollPane.HORIZONTAL_SCROLLBAR_NEVER);
        scrollPane.setPreferredSize(new Dimension(397,frame.getHeight()-25));
        scrollPane.createVerticalScrollBar();
        frame.add(scrollPane, BorderLayout.CENTER);
        update();
        frame.pack();
        frame.setVisible(true);
    }

    void update(){
        for (int i = 0; i < elements.size(); i++){
            if (!elements.get(i).getOnScreen()){
                elements.get(i).setOnScreen(true);
                elementPanel.add(elements.get(i));
            }
            else if (elements.get(i).getOnScreen()){
                if (elements.get(i).toRemove){
                    elementPanel.remove(elements.get(i));
                    elements.remove(i);
                }
            }
        }
        msgSaver.saveElements(elements);
        frame.pack();
        elementPanel.updateUI();
        frame.setVisible(true);
    }

    void addElement(DisplayElem element){
        element.setPage(this);
        elements.add(element);
        update();
    }

    String chooseFolder(){
        String filepath;
        JFileChooser fileChooser = new JFileChooser();
        fileChooser.setFileSelectionMode(JFileChooser.DIRECTORIES_ONLY);
        fileChooser.showOpenDialog(frame);
        File selectedfile = fileChooser.getSelectedFile();
        filepath = selectedfile.getAbsolutePath();
        return filepath;
    }

    private String chooseFile(String filename, String description, String extensions){
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
        runMenu = new JMenu("Run");
        menubar.add(runMenu);

        JMenuItem startup = new JMenuItem("Run on Startup...");
        startup.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) { }
            @Override public void mouseExited(MouseEvent e) { }
            @Override public void mouseReleased(MouseEvent e) {
                startupFrame();
            }
        });
        runMenu.add(startup);
        runMenu.addSeparator();

        for (GlobalSettings settings : settingsFiles) {
            createCfgRunMenu(settings);
        }

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

    private void createCfgRunMenu(GlobalSettings settings) {
        JMenuItem run = new JMenuItem("Run " + settings.getFileName());
        run.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) { }
            @Override public void mouseExited(MouseEvent e) { }
            @Override public void mouseReleased(MouseEvent e) {
                ImageSaver imageSaver = new ImageSaver(settings.getSaveFolder());
                EmailGetter getter = new EmailGetter(settings.getPopHost(), settings.getStoreType(), settings.getAccount(), settings.getPassword(),imageSaver);
                DisplayElem email = getter.fetch(settings.getKeywords());
                addElement(email);
            }
        });
        runMenu.add(run);
    }

    private void startupFrame() {
        JFrame startupChooser = new JFrame("onStart");
        startupChooser.setDefaultCloseOperation(JFrame.DISPOSE_ON_CLOSE);
        startupChooser.setSize(200,50 + settingsFiles.size()*20);
        startupChooser.setLocation(300,200);
        startupChooser.setLayout(new BorderLayout());
        JPanel panel = new JPanel();
        panel.setLayout(new BoxLayout(panel,BoxLayout.PAGE_AXIS));
        for(GlobalSettings s: settingsFiles){
            JCheckBox box = new JCheckBox(s.getFileName());
            box.setSelected(s.getOnstartup());
            box.addActionListener(e -> {
                s.setOnStartup(box.isSelected());
                System.out.println(box.isSelected());
                System.out.println(s.getOnstartup());
            });
            panel.add(box);
        }
        startupChooser.add(panel, BorderLayout.CENTER);
        JButton done = new JButton("Done");
        done.addActionListener(e -> {
            for (GlobalSettings s: settingsFiles){
                s.saveData();
            }
            startupChooser.dispose();
        });
        JPanel south = new JPanel(new FlowLayout());
        south.add(done);
        south.setOpaque(true);
        startupChooser.add(south, BorderLayout.SOUTH);
        startupChooser.pack();
        startupChooser.setVisible(true);
    }

    private JMenu generateFileMenu(){
        filemenu = new JMenu("File");

        //===CREATE NEW===
        JMenuItem createNew = new JMenuItem("Create Profile");
        createNew.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                GlobalSettings settings = new GlobalSettings("");

                JFrame filenameFrame = new JFrame("Filename");
                filenameFrame.setDefaultCloseOperation(JFrame.DISPOSE_ON_CLOSE);
                filenameFrame.setSize(300,100);
                filenameFrame.setLocation(300,200);
                filenameFrame.setLayout(new BorderLayout());
                JPanel panel = new JPanel();
                panel.setLayout(new FlowLayout());
                JTextField field = new JTextField();
                field.setPreferredSize(new Dimension(200,20));
                panel.add(field);
                filenameFrame.add(panel, BorderLayout.CENTER);
                JButton done = new JButton("Done");
                done.addActionListener(f -> {
                    String substring;
                    if (field.getText().contains(".")){
                        substring = field.getText().substring(0,field.getText().lastIndexOf("."));
                    }
                    else {
                        substring = field.getText();
                    }
                    settings.setCfgFilepath("cfg/" + substring + ".cfg");
                    filenameFrame.dispose();
                    settings.setSaveFolder(chooseFolder());
                    PreferencesFrame prefFrame = new PreferencesFrame();
                    prefFrame.updatePrefrences(settings);
                    settings.saveData();
                    settingsFiles.add(settings);
                    createCfgFileMenu(settings);
                    createCfgRunMenu(settings);
                    frame.pack();
                });
                JPanel south = new JPanel(new FlowLayout());
                south.add(done);
                south.setOpaque(true);
                filenameFrame.add(south, BorderLayout.SOUTH);
                filenameFrame.pack();
                filenameFrame.setVisible(true);
            }});
        filemenu.add(createNew);

        //===IMPORT SETTINGS====
        JMenuItem importSettings = new JMenuItem("Import Settings");
        importSettings.addMouseListener(new MouseListener(){
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e)  {}
            @Override public void mouseReleased(MouseEvent e) {
                String filepath = chooseFile("settings.cfg",".cfg", "cfg");
                File file = new File(filepath);
                GlobalSettings settings = new GlobalSettings(filepath);
                settings.getData();
                settings.setCfgFilepath("cfg/" + file.getName());
                settings.saveData();
                createCfgFileMenu(settings);
                createCfgRunMenu(settings);
            }});
        filemenu.add(importSettings);
        filemenu.addSeparator();

        for (GlobalSettings settings : settingsFiles) {
            createCfgFileMenu(settings);
        }
        return filemenu;
    }

    private void createCfgFileMenu(GlobalSettings settings) {
        JMenu subMenu = new JMenu(settings.getFileName());

        //===EXPORT SETTINGS====
        JMenuItem exportSettings = new JMenuItem("Export Settings");
        exportSettings.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e) {}
            @Override public void mouseReleased(MouseEvent e) {
                String file = chooseFile("export.cfg", ".cfg", "cfg");
                if (file != null) {
                    String oldPath = settings.getCfgFilepath();
                    settings.setCfgFilepath(file);
                    settings.saveData();
                    settings.setCfgFilepath(oldPath);
                }
            }
        });
        subMenu.add(exportSettings);
        //subMenu.addSeparator();

        //===SAVE FOLDER====
        JMenuItem saveFolder = new JMenuItem("Picture Save Folder");
        saveFolder.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e) {}
            @Override public void mouseReleased(MouseEvent e) {
                String folder = chooseFolder();
                if (folder != null) {
                    settings.setSaveFolder(folder);
                }
            }
        });
        subMenu.add(saveFolder);


        //===PREFRENCES====
        JMenuItem prefs = new JMenuItem("Prefrences");
        filemenu.add(prefs);
        prefs.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e) {}
            @Override public void mouseReleased(MouseEvent e) {
                PreferencesFrame prefFrame = new PreferencesFrame();
                prefFrame.updatePrefrences(settings);
            }
        });
        subMenu.add(prefs);
        filemenu.add(subMenu);
    }
}
