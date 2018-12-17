package com.horine.emailAttachmentDownloader;


import javax.imageio.ImageIO;
import javax.swing.*;
import java.awt.*;
import java.awt.event.MouseEvent;
import java.awt.event.MouseListener;
import java.awt.image.BufferedImage;
import java.io.File;
import java.io.IOException;

public class DisplayElem extends JPanel{

    int id;
    String text;
    boolean onScreen;
    boolean toRemove;
    DisplayPage page;

    public DisplayElem(DisplayPage p, int id, String text){
        super();
        this.page = p;
        this.id = id;
        this.text = text;
        onScreen = false;
        toRemove = false;

        setMaximumSize(new Dimension(400,100));
        setMinimumSize(new Dimension(397,50));

        setLayout(new BorderLayout());
        //setPreferredSize(new Dimension(397,50));
        setBackground(Color.ORANGE);
        setToolTipText(text);

        addCloseButton();
    }

    private void close(){
        toRemove = true;
        page.update();
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
