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
    }
}