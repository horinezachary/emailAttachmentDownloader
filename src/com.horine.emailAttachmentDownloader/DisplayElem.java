package com.horine.emailAttachmentDownloader;


import javax.imageio.ImageIO;
import javax.swing.*;
import java.awt.*;
import java.awt.event.MouseEvent;
import java.awt.event.MouseListener;
import java.awt.image.BufferedImage;
import java.io.File;
import java.io.IOException;
import java.text.SimpleDateFormat;
import java.util.Date;

public class DisplayElem extends JPanel{

    int id;
    String date;
    String text;
    boolean onScreen;
    boolean toRemove;
    DisplayPage page;

    public DisplayElem(DisplayPage p, int id, String text){
        super();
        this.page = p;
        this.id = id;
        SimpleDateFormat sdf = new SimpleDateFormat("MM/dd/yy HH:mm");
        Date dt = new Date();
        this.date = sdf.format(dt);
        onScreen = false;
        toRemove = false;
        setupFrame();
    }

    public DisplayElem(DisplayPage p, int id, String date, String text){
        super();
        this.page = p;
        this.id = id;
        this.date = date;
        this.text = text;
        onScreen = false;
        toRemove = false;
        setupFrame();
    }
    private void setupFrame(){
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

    public int getId(){
        return id;
    }

    public void setId(int id){
        this.id = id;
    }

    public String getDate(){
        return this.date;
    }

    public String getText() {
        return text;
    }

    private void addCloseButton(){
        JButton closeButton = new JButton("X");
        try {
            BufferedImage buttonIcon = ImageIO.read(new File("close.png"));
            closeButton = new JButton(new ImageIcon(buttonIcon));
            closeButton.addMouseListener(new MouseListener() {
                @Override public void mouseClicked(MouseEvent e) {}
                @Override public void mousePressed(MouseEvent e) {}
                @Override public void mouseReleased(MouseEvent e) {close();}
                @Override public void mouseEntered(MouseEvent e) {}
                @Override public void mouseExited(MouseEvent e) {}
            });

        } catch (IOException e) {
            e.printStackTrace();
        }
        closeButton.setMaximumSize(new Dimension(20,20));
        //closeButton.setBorder(BorderFactory.createEmptyBorder());
        //closeButton.setContentAreaFilled(false);
        add(closeButton,BorderLayout.EAST);
    }
}
