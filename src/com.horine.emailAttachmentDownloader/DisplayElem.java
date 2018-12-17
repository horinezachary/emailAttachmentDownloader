package com.horine.emailAttachmentDownloader;


import javax.swing.*;

public class DisplayElem extends JPanel{

    int id;
    String text;
    boolean onScreen;
    boolean toRemove;
    DisplayPage page;


    public DisplayElem(DisplayPage p, int id, String text){
        this.page = p;
        this.id = id;
        this.text = text;
        onScreen = false;
        toRemove = false;

    }

    private void close(){
        toRemove = true;
    }

    public void setOnScreen(boolean onScreen) {
        this.onScreen = onScreen;
    }
    public boolean getOnScreen(){
        return onScreen;
    }

    public void setId(int id){
        this.id = id;
    }
}
