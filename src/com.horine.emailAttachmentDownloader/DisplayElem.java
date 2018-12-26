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

    private String date;
    private String text;
    private boolean onScreen;
    boolean toRemove;
    private DisplayPage page;

    public DisplayElem(){
        super();
        this.page = null;
        SimpleDateFormat sdf = new SimpleDateFormat("MM/dd/yy HH:mm");
        Date dt = new Date();
        this.date = sdf.format(dt);
        this.text = "";
        onScreen = false;
        toRemove = false;
        setupFrame();
    }

    DisplayElem(String text){
        super();
        SimpleDateFormat sdf = new SimpleDateFormat("MM/dd/yy hh:mm a");
        Date dt = new Date();
        this.date = sdf.format(dt);
        this.text = text;
        onScreen = false;
        toRemove = false;
        setupFrame();
    }

    DisplayElem(String date, String text){
        super();
        this.date = date;
        this.text = text;
        onScreen = false;
        toRemove = false;
        setupFrame();
    }
    private void setupFrame(){
        setMaximumSize(new Dimension(370,100));
        setMinimumSize(new Dimension(370,100));
        setPreferredSize(new Dimension(370, 30 + (int)(Math.ceil((double)text.length()/40) * 16) + 10));
        setLayout(new BorderLayout());
        setBorder(BorderFactory.createBevelBorder(0));
        setBackground(Color.ORANGE);
        add(setupLeftPane());
        addCloseButton();
    }

    private void close(){
        toRemove = true;
        page.update();
    }

    void setOnScreen(boolean onScreen) {
        this.onScreen = onScreen;
    }
    boolean getOnScreen(){
        return onScreen;
    }

    void setPage(DisplayPage page){
        this.page = page;
    }

    String getDate(){
        return this.date;
    }

    String getText() {
        return text;
    }

    private void addCloseButton(){
        BufferedImage buttonIcon = null;
        try {
            buttonIcon = ImageIO.read(new File("src/close.png"));
        } catch (IOException e) {
            e.printStackTrace();
        }
        JButton closeButton = new JButton(new ImageIcon(buttonIcon));
        closeButton.addMouseListener(new MouseListener() {
            @Override public void mouseClicked(MouseEvent e) {}
            @Override public void mousePressed(MouseEvent e) {}
            @Override public void mouseReleased(MouseEvent e) {close();}
            @Override public void mouseEntered(MouseEvent e) {}
            @Override public void mouseExited(MouseEvent e) {}
        });
        closeButton.setMaximumSize(new Dimension(20,20));
        closeButton.setPreferredSize(new Dimension(20,20));
        closeButton.setMinimumSize(new Dimension(20,20));
        closeButton.setBorder(BorderFactory.createEmptyBorder());
        closeButton.setContentAreaFilled(false);
        JPanel p = new JPanel();
        p.setLayout(new FlowLayout());
        p.setBackground(this.getBackground());
        p.add(closeButton);
        add(p,BorderLayout.EAST);
    }

    private JPanel setupLeftPane(){
        JPanel panel = new JPanel();
        panel.setLayout(new BorderLayout());
        JTextPane datePane = new JTextPane();
        datePane.setText(date);
        datePane.setEditable(false);
        datePane.setBackground(this.getBackground());
        datePane.setMargin(new Insets(5,5,0,5));
        JTextPane textPane = new JTextPane();
        textPane.setText(text);
        textPane.setEditable(false);
        textPane.setBackground(this.getBackground());
        textPane.setMargin(new Insets(5,15,5,5));
        panel.add(datePane, BorderLayout.NORTH);
        panel.add(textPane, BorderLayout.CENTER);

        return panel;
    }
}
