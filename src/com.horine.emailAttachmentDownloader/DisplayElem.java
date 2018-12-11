package com.horine.emailAttachmentDownloader;


import javax.swing.*;

public class DisplayElem {

    int id;
    String text;

    public DisplayElem(int id, String text){
        this.id = id;
        this.text = text;
    }

    public JPanel getJPanel(){
        JPanel panel = new JPanel();
        JTextPane textpane = new JTextPane();
        textpane.setText(text);

        panel.add(textpane);
        return panel;
    }
}
