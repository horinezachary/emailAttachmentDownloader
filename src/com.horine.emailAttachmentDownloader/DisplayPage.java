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
    }
    }
}
